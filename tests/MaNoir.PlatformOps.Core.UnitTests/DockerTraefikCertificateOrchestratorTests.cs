using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class DockerTraefikCertificateOrchestratorTests
{
	[TestMethod]
	public async Task SaveAndLoadAsync_ShouldPersistCertificateMaterialUnderTraefikDirectory()
	{
		string sharedServicesRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "shared-services");
		DockerTraefikCertificateStore store = new DockerTraefikCertificateStore(sharedServicesRootPath);
		AcmeDnsCertificate certificate = new AcmeDnsCertificate()
		{
			OrderedDomainName = "*.manoir.app",
			AccountKeyPem = "account-key",
			CertificatePem = CreateSelfSignedCertificatePem(DateTimeOffset.UtcNow.AddDays(40)),
			FullChainPem = CreateSelfSignedCertificatePem(DateTimeOffset.UtcNow.AddDays(40)),
			PrivateKeyPem = "private-key"
		};

		try
		{
			await store.SaveAsync(certificate, CancellationToken.None);
			AcmeDnsCertificateState state = await store.LoadAsync(CancellationToken.None);

			string certificatesDirectoryPath = DockerTraefikCertificateStore.ResolveCertificatesDirectoryPath(sharedServicesRootPath);
			Assert.IsTrue(File.Exists(Path.Combine(certificatesDirectoryPath, DockerTraefikCertificateStore.AccountKeyFileName)));
			Assert.IsTrue(File.Exists(Path.Combine(certificatesDirectoryPath, DockerTraefikCertificateStore.OrderedDomainNameFileName)));
			Assert.AreEqual("*.manoir.app", state.OrderedDomainName);
			Assert.AreEqual("account-key", state.AccountKeyPem);
			Assert.AreEqual("private-key", state.PrivateKeyPem);
			Assert.IsTrue(state.NotAfterUtc > DateTimeOffset.UtcNow.AddDays(30));
		}
		finally
		{
			string homeAutomationRootPath = DockerSharedServicesCatalog.ResolveDockerHostHomeAutomationRootPath(sharedServicesRootPath);
			if (Directory.Exists(homeAutomationRootPath))
				Directory.Delete(homeAutomationRootPath, true);
		}
	}

	[TestMethod]
	public async Task EnsureCurrentAsync_ShouldRenewCertificateAndTriggerReloadWhenNeeded()
	{
		string sharedServicesRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "shared-services");
		string capturedContainerName = null;
		string capturedSignal = null;
		int issueCount = 0;

		try
		{
			DockerTraefikCertificateOrchestrator orchestrator = new DockerTraefikCertificateOrchestrator(
				dockerClient: null,
				sharedServicesRootPath: sharedServicesRootPath,
				resolveSecretAsync: (secretName, cancellationToken) => Task.FromResult(secretName switch
				{
					PlatformOpsSharedSecretNames.OvhDnsApplicationKey => "app-key",
					PlatformOpsSharedSecretNames.OvhDnsApplicationSecret => "app-secret",
					PlatformOpsSharedSecretNames.OvhDnsConsumerKey => "consumer-key",
					_ => null
				}),
				issueAsync: (request, options, writer, cancellationToken) =>
				{
					issueCount++;
					return Task.FromResult(new AcmeDnsCertificate()
					{
						OrderedDomainName = "*.manoir.app",
						AccountKeyPem = "renewed-account-key",
						CertificatePem = CreateSelfSignedCertificatePem(DateTimeOffset.UtcNow.AddDays(50)),
						FullChainPem = CreateSelfSignedCertificatePem(DateTimeOffset.UtcNow.AddDays(50)),
						PrivateKeyPem = "renewed-private-key"
					});
				},
				reloadAsync: (containerName, signal, fallbackToRestart, cancellationToken) =>
				{
					capturedContainerName = containerName;
					capturedSignal = signal;
					return Task.FromResult(true);
				},
				createStateStore: () => new DockerTraefikCertificateStore(sharedServicesRootPath));

			DockerTraefikCertificateDeploymentResult result = await orchestrator.EnsureCurrentAsync(
				new DockerTraefikCertificateConfiguration()
				{
					DomainName = "manoir.app",
					UseWildcard = true,
					AccountEmail = "ops@manoir.app",
					OvhDns = new OvhDnsChallengeSecretConfiguration() { ZoneName = "manoir.app" }
				},
				CancellationToken.None);

			Assert.AreEqual(1, issueCount);
			Assert.IsTrue(result.Certificate.WasRenewed);
			Assert.IsTrue(result.ReloadAttempted);
			Assert.IsTrue(result.ReloadPerformed);
			Assert.AreEqual(DockerTraefikCertificateOrchestrator.DefaultEdgeContainerName, capturedContainerName);
			Assert.AreEqual(DockerTraefikCertificateOrchestrator.DefaultReloadSignal, capturedSignal);

			AcmeDnsCertificateState state = await new DockerTraefikCertificateStore(sharedServicesRootPath).LoadAsync(CancellationToken.None);
			Assert.AreEqual("*.manoir.app", state.OrderedDomainName);
			Assert.AreEqual("renewed-account-key", state.AccountKeyPem);
			Assert.IsTrue(state.NotAfterUtc > DateTimeOffset.UtcNow.AddDays(40));
		}
		finally
		{
			string homeAutomationRootPath = DockerSharedServicesCatalog.ResolveDockerHostHomeAutomationRootPath(sharedServicesRootPath);
			if (Directory.Exists(homeAutomationRootPath))
				Directory.Delete(homeAutomationRootPath, true);
		}
	}

	private static string CreateSelfSignedCertificatePem(DateTimeOffset notAfterUtc)
	{
		using RSA rsa = RSA.Create(2048);
		CertificateRequest request = new CertificateRequest("CN=manoir.app", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), notAfterUtc);
		return certificate.ExportCertificatePem();
	}
}