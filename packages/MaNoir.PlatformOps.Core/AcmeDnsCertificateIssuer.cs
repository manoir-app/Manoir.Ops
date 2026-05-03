using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;

namespace MaNoir.PlatformOps.Core;

public interface IAcmeDnsTxtChallengeWriter
{
	Task UpsertTxtRecordAsync(string fullyQualifiedRecordName, string value, CancellationToken cancellationToken = default);
}

public sealed class AcmeDnsCertificateRequest
{
	public string DomainName { get; set; }

	public bool UseWildcard { get; set; }

	public string CountryName { get; set; } = "FR";

	public string State { get; set; } = "Nord";

	public string Locality { get; set; } = "Lille";

	public string OrganizationName { get; set; } = "maNoir";

	public string OrganizationUnit { get; set; } = "PlatformOps";

	public string PreferredChainName { get; set; } = "ISRG Root X1";
}

public sealed class AcmeDnsCertificateIssuerOptions
{
	public Uri ServerUri { get; set; } = WellKnownServers.LetsEncryptV2;

	public string AccountEmail { get; set; }

	public string AccountKeyPem { get; set; }

	public int InitialDnsPropagationDelaySeconds { get; set; } = 30;

	public int MaxValidationPollCount { get; set; } = 30;

	public int DefaultValidationPollDelaySeconds { get; set; } = 45;
}

public sealed class AcmeDnsCertificate
{
	public string RequestedDomainName { get; set; }

	public string OrderedDomainName { get; set; }

	public string DnsChallengeRecordName { get; set; }

	public string DnsChallengeRecordValue { get; set; }

	public string AccountKeyPem { get; set; }

	public string CertificatePem { get; set; }

	public string FullChainPem { get; set; }

	public string PrivateKeyPem { get; set; }

	public byte[] CertificateDer { get; set; }

	public byte[] PrivateKeyDer { get; set; }
}

public static class AcmeDnsCertificateIssuer
{
	public static string NormalizeOrderedDomainName(string domainName, bool useWildcard)
	{
		if (string.IsNullOrWhiteSpace(domainName))
			throw new ArgumentException("A domain name is required.", nameof(domainName));

		string normalizedDomainName = domainName.Trim().Trim('.');
		if (!useWildcard)
			return normalizedDomainName;

		return normalizedDomainName.StartsWith("*.", StringComparison.Ordinal)
			? normalizedDomainName
			: "*." + normalizedDomainName;
	}

	public static string BuildDnsChallengeRecordName(string orderedDomainName)
	{
		if (string.IsNullOrWhiteSpace(orderedDomainName))
			throw new ArgumentException("An ordered domain name is required.", nameof(orderedDomainName));

		string normalizedDomainName = orderedDomainName.Trim().Trim('.');
		if (normalizedDomainName.StartsWith("*.", StringComparison.Ordinal))
			normalizedDomainName = normalizedDomainName.Substring(2);

		return "_acme-challenge." + normalizedDomainName;
	}

	public static async Task<AcmeDnsCertificate> IssueAsync(
		AcmeDnsCertificateRequest request,
		AcmeDnsCertificateIssuerOptions options,
		IAcmeDnsTxtChallengeWriter dnsTxtChallengeWriter,
		CancellationToken cancellationToken = default)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		if (options == null)
			throw new ArgumentNullException(nameof(options));

		if (dnsTxtChallengeWriter == null)
			throw new ArgumentNullException(nameof(dnsTxtChallengeWriter));

		if (options.MaxValidationPollCount <= 0)
			throw new ArgumentOutOfRangeException(nameof(options.MaxValidationPollCount), "The validation poll count must be greater than zero.");

		if (options.DefaultValidationPollDelaySeconds <= 0)
			throw new ArgumentOutOfRangeException(nameof(options.DefaultValidationPollDelaySeconds), "The validation poll delay must be greater than zero.");

		string orderedDomainName = NormalizeOrderedDomainName(request.DomainName, request.UseWildcard);
		string dnsChallengeRecordName = BuildDnsChallengeRecordName(orderedDomainName);

		AcmeContext acmeContext;
		if (string.IsNullOrWhiteSpace(options.AccountKeyPem))
		{
			if (string.IsNullOrWhiteSpace(options.AccountEmail))
				throw new ArgumentException("An ACME account email is required when no existing account key is provided.", nameof(options));

			acmeContext = new AcmeContext(options.ServerUri ?? WellKnownServers.LetsEncryptV2);
			await acmeContext.NewAccount(options.AccountEmail.Trim(), true);
		}
		else
		{
			IKey existingAccountKey = KeyFactory.FromPem(options.AccountKeyPem);
			acmeContext = new AcmeContext(options.ServerUri ?? WellKnownServers.LetsEncryptV2, existingAccountKey);
			await acmeContext.Account();
		}

		IOrderContext order = await acmeContext.NewOrder([orderedDomainName]);
		IAuthorizationContext authorization = (await order.Authorizations()).First();
		IChallengeContext dnsChallenge = await authorization.Dns();
		string dnsChallengeRecordValue = acmeContext.AccountKey.DnsTxt(dnsChallenge.Token);

		await dnsTxtChallengeWriter.UpsertTxtRecordAsync(dnsChallengeRecordName, dnsChallengeRecordValue, cancellationToken);

		if (options.InitialDnsPropagationDelaySeconds > 0)
			await Task.Delay(TimeSpan.FromSeconds(options.InitialDnsPropagationDelaySeconds), cancellationToken);

		for (int attempt = 0; attempt < options.MaxValidationPollCount; attempt++)
		{
			Challenge challenge = await dnsChallenge.Validate();
			ChallengeStatus status = challenge.Status.GetValueOrDefault(ChallengeStatus.Pending);

			if (status == ChallengeStatus.Valid)
				return await GenerateCertificateAsync(request, orderedDomainName, dnsChallengeRecordName, dnsChallengeRecordValue, acmeContext, order);

			if (status == ChallengeStatus.Invalid)
				throw new InvalidOperationException("The ACME DNS challenge was rejected for '" + orderedDomainName + "'.");

			int retryAfterSeconds = dnsChallenge.RetryAfter > 0
				? dnsChallenge.RetryAfter
				: options.DefaultValidationPollDelaySeconds;

			await Task.Delay(TimeSpan.FromSeconds(retryAfterSeconds), cancellationToken);
		}

		throw new TimeoutException("The ACME DNS challenge did not validate in the expected time window for '" + orderedDomainName + "'.");
	}

	private static async Task<AcmeDnsCertificate> GenerateCertificateAsync(
		AcmeDnsCertificateRequest request,
		string orderedDomainName,
		string dnsChallengeRecordName,
		string dnsChallengeRecordValue,
		AcmeContext acmeContext,
		IOrderContext order)
	{
		IKey privateKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
		var certificateChain = await order.Generate(
			new CsrInfo()
			{
				CountryName = request.CountryName,
				State = request.State,
				Locality = request.Locality,
				Organization = request.OrganizationName,
				OrganizationUnit = request.OrganizationUnit,
				CommonName = orderedDomainName
			},
			privateKey,
			request.PreferredChainName);

		return new AcmeDnsCertificate()
		{
			RequestedDomainName = request.DomainName,
			OrderedDomainName = orderedDomainName,
			DnsChallengeRecordName = dnsChallengeRecordName,
			DnsChallengeRecordValue = dnsChallengeRecordValue,
			AccountKeyPem = acmeContext.AccountKey.ToPem(),
			CertificatePem = certificateChain.Certificate.ToPem(),
			FullChainPem = certificateChain.ToPem(),
			PrivateKeyPem = privateKey.ToPem(),
			CertificateDer = certificateChain.Certificate.ToDer(),
			PrivateKeyDer = privateKey.ToDer()
		};
	}
}