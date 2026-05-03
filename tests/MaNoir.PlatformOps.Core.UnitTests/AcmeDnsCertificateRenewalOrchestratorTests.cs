using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class AcmeDnsCertificateRenewalOrchestratorTests
{
	[TestMethod]
	public async Task EnsureCurrentAsync_ShouldSkipRenewalWhenCertificateIsFresh()
	{
		InMemoryCertificateStateStore stateStore = new InMemoryCertificateStateStore(new AcmeDnsCertificateState()
		{
			OrderedDomainName = "*.manoir.app",
			CertificatePem = CreateSelfSignedCertificatePem(DateTimeOffset.UtcNow.AddDays(30))
		});
		int issuedCount = 0;
		AcmeDnsCertificateRenewalOrchestrator orchestrator = new AcmeDnsCertificateRenewalOrchestrator(
			stateStore,
			(request, options, writer, cancellationToken) =>
			{
				issuedCount++;
				return Task.FromResult<AcmeDnsCertificate>(null);
			});

		AcmeDnsCertificateRenewalResult result = await orchestrator.EnsureCurrentAsync(
			new AcmeDnsCertificateRequest() { DomainName = "manoir.app", UseWildcard = true },
			new AcmeDnsCertificateIssuerOptions() { AccountEmail = "ops@manoir.app" },
			new NoOpDnsTxtChallengeWriter(),
			cancellationToken: CancellationToken.None);

		Assert.IsFalse(result.WasRenewed);
		Assert.AreEqual(0, issuedCount);
		Assert.IsTrue(result.EffectiveExpirationUtc > DateTimeOffset.UtcNow.AddDays(20));
	}

	[TestMethod]
	public async Task EnsureCurrentAsync_ShouldRenewWhenRequestedDomainChangesEvenIfCertificateIsFresh()
	{
		InMemoryCertificateStateStore stateStore = new InMemoryCertificateStateStore(new AcmeDnsCertificateState()
		{
			OrderedDomainName = "*.manoir.app",
			CertificatePem = CreateSelfSignedCertificatePem(DateTimeOffset.UtcNow.AddDays(30))
		});
		int issuedCount = 0;
		AcmeDnsCertificateRenewalOrchestrator orchestrator = new AcmeDnsCertificateRenewalOrchestrator(
			stateStore,
			(request, options, writer, cancellationToken) =>
			{
				issuedCount++;
				return Task.FromResult(new AcmeDnsCertificate()
				{
					OrderedDomainName = "*.autre.manoir.app",
					AccountKeyPem = "renewed-account-key",
					CertificatePem = CreateSelfSignedCertificatePem(DateTimeOffset.UtcNow.AddDays(45)),
					FullChainPem = CreateSelfSignedCertificatePem(DateTimeOffset.UtcNow.AddDays(45)),
					PrivateKeyPem = "private-key"
				});
			});

		AcmeDnsCertificateRenewalResult result = await orchestrator.EnsureCurrentAsync(
			new AcmeDnsCertificateRequest() { DomainName = "autre.manoir.app", UseWildcard = true },
			new AcmeDnsCertificateIssuerOptions() { AccountEmail = "ops@manoir.app" },
			new NoOpDnsTxtChallengeWriter(),
			cancellationToken: CancellationToken.None);

		Assert.IsTrue(result.WasRenewed);
		Assert.AreEqual(1, issuedCount);
		Assert.AreEqual("*.autre.manoir.app", stateStore.State.OrderedDomainName);
	}

	[TestMethod]
	public async Task EnsureCurrentAsync_ShouldRenewAndReuseStoredAccountKey()
	{
		InMemoryCertificateStateStore stateStore = new InMemoryCertificateStateStore(new AcmeDnsCertificateState()
		{
			AccountKeyPem = "stored-account-key"
		});
		string capturedAccountKeyPem = null;
		AcmeDnsCertificate issuedCertificate = new AcmeDnsCertificate()
		{
			OrderedDomainName = "*.manoir.app",
			AccountKeyPem = "renewed-account-key",
			CertificatePem = CreateSelfSignedCertificatePem(DateTimeOffset.UtcNow.AddDays(45)),
			FullChainPem = CreateSelfSignedCertificatePem(DateTimeOffset.UtcNow.AddDays(45)),
			PrivateKeyPem = "private-key"
		};

		AcmeDnsCertificateRenewalOrchestrator orchestrator = new AcmeDnsCertificateRenewalOrchestrator(
			stateStore,
			(request, options, writer, cancellationToken) =>
			{
				capturedAccountKeyPem = options.AccountKeyPem;
				return Task.FromResult(issuedCertificate);
			});

		AcmeDnsCertificateRenewalResult result = await orchestrator.EnsureCurrentAsync(
			new AcmeDnsCertificateRequest() { DomainName = "manoir.app", UseWildcard = true },
			new AcmeDnsCertificateIssuerOptions() { AccountEmail = "ops@manoir.app" },
			new NoOpDnsTxtChallengeWriter(),
			cancellationToken: CancellationToken.None);

		Assert.IsTrue(result.WasRenewed);
		Assert.AreEqual("stored-account-key", capturedAccountKeyPem);
		Assert.AreEqual("renewed-account-key", stateStore.State.AccountKeyPem);
		Assert.IsTrue(result.EffectiveExpirationUtc > DateTimeOffset.UtcNow.AddDays(30));
	}

	private static string CreateSelfSignedCertificatePem(DateTimeOffset notAfterUtc)
	{
		using RSA rsa = RSA.Create(2048);
		CertificateRequest request = new CertificateRequest("CN=manoir.app", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), notAfterUtc);
		return certificate.ExportCertificatePem();
	}

	private sealed class InMemoryCertificateStateStore : IAcmeDnsCertificateStateStore
	{
		public InMemoryCertificateStateStore(AcmeDnsCertificateState state)
		{
			State = state;
		}

		public AcmeDnsCertificateState State { get; private set; }

		public Task<AcmeDnsCertificateState> LoadAsync(CancellationToken cancellationToken = default)
		{
			if (State != null)
				State.NotAfterUtc = AcmeDnsCertificateRenewalOrchestrator.TryGetNotAfterUtc(State.CertificatePem ?? State.FullChainPem);

			return Task.FromResult(State);
		}

		public Task SaveAsync(AcmeDnsCertificate certificate, CancellationToken cancellationToken = default)
		{
			State = new AcmeDnsCertificateState()
			{
				OrderedDomainName = certificate.OrderedDomainName,
				AccountKeyPem = certificate.AccountKeyPem,
				CertificatePem = certificate.CertificatePem,
				FullChainPem = certificate.FullChainPem,
				PrivateKeyPem = certificate.PrivateKeyPem,
				NotAfterUtc = AcmeDnsCertificateRenewalOrchestrator.TryGetNotAfterUtc(certificate.CertificatePem ?? certificate.FullChainPem)
			};
			return Task.CompletedTask;
		}
	}

	private sealed class NoOpDnsTxtChallengeWriter : IAcmeDnsTxtChallengeWriter
	{
		public Task UpsertTxtRecordAsync(string fullyQualifiedRecordName, string value, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}
}