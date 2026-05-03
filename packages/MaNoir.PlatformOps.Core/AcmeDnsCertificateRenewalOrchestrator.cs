using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.PlatformOps.Core;

public interface IAcmeDnsCertificateStateStore
{
	Task<AcmeDnsCertificateState> LoadAsync(CancellationToken cancellationToken = default);

	Task SaveAsync(AcmeDnsCertificate certificate, CancellationToken cancellationToken = default);
}

public sealed class AcmeDnsCertificateState
{
	public string OrderedDomainName { get; set; }

	public string AccountKeyPem { get; set; }

	public string CertificatePem { get; set; }

	public string FullChainPem { get; set; }

	public string PrivateKeyPem { get; set; }

	public DateTimeOffset? NotAfterUtc { get; set; }
}

public sealed class AcmeDnsCertificateRenewalPolicy
{
	public TimeSpan RenewBefore { get; set; } = TimeSpan.FromDays(5);
}

public sealed class AcmeDnsCertificateRenewalResult
{
	public bool WasRenewed { get; set; }

	public DateTimeOffset? PreviousExpirationUtc { get; set; }

	public DateTimeOffset? EffectiveExpirationUtc { get; set; }

	public AcmeDnsCertificateState State { get; set; }
}

public sealed class AcmeDnsCertificateRenewalOrchestrator
{
	private readonly IAcmeDnsCertificateStateStore _stateStore;
	private readonly Func<AcmeDnsCertificateRequest, AcmeDnsCertificateIssuerOptions, IAcmeDnsTxtChallengeWriter, CancellationToken, Task<AcmeDnsCertificate>> _issueAsync;

	public AcmeDnsCertificateRenewalOrchestrator(IAcmeDnsCertificateStateStore stateStore)
		: this(stateStore, AcmeDnsCertificateIssuer.IssueAsync)
	{
	}

	public AcmeDnsCertificateRenewalOrchestrator(
		IAcmeDnsCertificateStateStore stateStore,
		Func<AcmeDnsCertificateRequest, AcmeDnsCertificateIssuerOptions, IAcmeDnsTxtChallengeWriter, CancellationToken, Task<AcmeDnsCertificate>> issueAsync)
	{
		_stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
		_issueAsync = issueAsync ?? throw new ArgumentNullException(nameof(issueAsync));
	}

	public async Task<AcmeDnsCertificateRenewalResult> EnsureCurrentAsync(
		AcmeDnsCertificateRequest request,
		AcmeDnsCertificateIssuerOptions options,
		IAcmeDnsTxtChallengeWriter dnsTxtChallengeWriter,
		AcmeDnsCertificateRenewalPolicy policy = null,
		CancellationToken cancellationToken = default)
	{
		if (request == null)
			throw new ArgumentNullException(nameof(request));

		if (options == null)
			throw new ArgumentNullException(nameof(options));

		if (dnsTxtChallengeWriter == null)
			throw new ArgumentNullException(nameof(dnsTxtChallengeWriter));

		AcmeDnsCertificateRenewalPolicy effectivePolicy = policy ?? new AcmeDnsCertificateRenewalPolicy();
		if (effectivePolicy.RenewBefore < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(policy), "The renewal threshold must be positive or zero.");

		string requestedOrderedDomainName = AcmeDnsCertificateIssuer.NormalizeOrderedDomainName(request.DomainName, request.UseWildcard);
		AcmeDnsCertificateState currentState = await _stateStore.LoadAsync(cancellationToken) ?? new AcmeDnsCertificateState();
		DateTimeOffset? currentExpirationUtc = currentState.NotAfterUtc ?? TryGetNotAfterUtc(currentState.CertificatePem ?? currentState.FullChainPem);
		bool requiresDomainRenewal = !string.Equals(currentState.OrderedDomainName, requestedOrderedDomainName, StringComparison.OrdinalIgnoreCase);

		if (!requiresDomainRenewal && currentExpirationUtc.HasValue && currentExpirationUtc.Value > DateTimeOffset.UtcNow.Add(effectivePolicy.RenewBefore))
		{
			currentState.NotAfterUtc = currentExpirationUtc;
			return new AcmeDnsCertificateRenewalResult()
			{
				WasRenewed = false,
				PreviousExpirationUtc = currentExpirationUtc,
				EffectiveExpirationUtc = currentExpirationUtc,
				State = currentState
			};
		}

		AcmeDnsCertificateIssuerOptions effectiveOptions = CloneOptions(options);
		if (string.IsNullOrWhiteSpace(effectiveOptions.AccountKeyPem) && !string.IsNullOrWhiteSpace(currentState.AccountKeyPem))
			effectiveOptions.AccountKeyPem = currentState.AccountKeyPem;

		AcmeDnsCertificate certificate = await _issueAsync(request, effectiveOptions, dnsTxtChallengeWriter, cancellationToken);
		certificate.OrderedDomainName ??= requestedOrderedDomainName;
		await _stateStore.SaveAsync(certificate, cancellationToken);

		AcmeDnsCertificateState renewedState = await _stateStore.LoadAsync(cancellationToken) ?? CreateState(certificate);
		DateTimeOffset? renewedExpirationUtc = renewedState.NotAfterUtc ?? TryGetNotAfterUtc(renewedState.CertificatePem ?? renewedState.FullChainPem);
		renewedState.NotAfterUtc = renewedExpirationUtc;

		return new AcmeDnsCertificateRenewalResult()
		{
			WasRenewed = true,
			PreviousExpirationUtc = currentExpirationUtc,
			EffectiveExpirationUtc = renewedExpirationUtc,
			State = renewedState
		};
	}

	public static DateTimeOffset? TryGetNotAfterUtc(string certificatePem)
	{
		if (string.IsNullOrWhiteSpace(certificatePem))
			return null;

		try
		{
			using X509Certificate2 certificate = X509Certificate2.CreateFromPem(certificatePem);
			return new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
		}
		catch (CryptographicException)
		{
			return null;
		}
	}

	private static AcmeDnsCertificateIssuerOptions CloneOptions(AcmeDnsCertificateIssuerOptions options)
	{
		return new AcmeDnsCertificateIssuerOptions()
		{
			ServerUri = options.ServerUri,
			AccountEmail = options.AccountEmail,
			AccountKeyPem = options.AccountKeyPem,
			InitialDnsPropagationDelaySeconds = options.InitialDnsPropagationDelaySeconds,
			MaxValidationPollCount = options.MaxValidationPollCount,
			DefaultValidationPollDelaySeconds = options.DefaultValidationPollDelaySeconds
		};
	}

	private static AcmeDnsCertificateState CreateState(AcmeDnsCertificate certificate)
	{
		return new AcmeDnsCertificateState()
		{
			OrderedDomainName = certificate.OrderedDomainName,
			AccountKeyPem = certificate.AccountKeyPem,
			CertificatePem = certificate.CertificatePem,
			FullChainPem = certificate.FullChainPem,
			PrivateKeyPem = certificate.PrivateKeyPem,
			NotAfterUtc = TryGetNotAfterUtc(certificate.CertificatePem ?? certificate.FullChainPem)
		};
	}
}