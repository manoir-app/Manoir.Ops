using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public sealed class DockerTraefikCertificateStore : IAcmeDnsCertificateStateStore
{
	public const string RelativeCertificatesDirectoryPath = "edge/traefik/certificates";

	public const string CertificateFileStem = "wildcard";

	public const string AccountKeyFileName = "acme-account.pem";

	public const string OrderedDomainNameFileName = "requested-domain.txt";

	public const string CertificatePemFileName = CertificateFileStem + ".pem.crt";

	public const string FullChainPemFileName = CertificateFileStem + ".fullchain.pem";

	public const string PrivateKeyPemFileName = CertificateFileStem + ".pem.key";

	private readonly string _sharedServicesRootPath;

	public DockerTraefikCertificateStore(string sharedServicesRootPath)
	{
		_sharedServicesRootPath = DockerSharedServicesCatalog.ResolveSharedServicesRootPath(sharedServicesRootPath);
	}

	public static string ResolveCertificatesDirectoryPath(string sharedServicesRootPath)
	{
		string homeAutomationRootPath = DockerSharedServicesCatalog.ResolveDockerHostHomeAutomationRootPath(sharedServicesRootPath);
		return Path.Combine(homeAutomationRootPath, "edge", "traefik", "certificates");
	}

	public static string ResolveAccountKeyPath(string sharedServicesRootPath)
	{
		return Path.Combine(ResolveCertificatesDirectoryPath(sharedServicesRootPath), AccountKeyFileName);
	}

	public static string ResolveCertificatePemPath(string sharedServicesRootPath)
	{
		return Path.Combine(ResolveCertificatesDirectoryPath(sharedServicesRootPath), CertificatePemFileName);
	}

	public static string ResolveOrderedDomainNamePath(string sharedServicesRootPath)
	{
		return Path.Combine(ResolveCertificatesDirectoryPath(sharedServicesRootPath), OrderedDomainNameFileName);
	}

	public static string ResolveFullChainPemPath(string sharedServicesRootPath)
	{
		return Path.Combine(ResolveCertificatesDirectoryPath(sharedServicesRootPath), FullChainPemFileName);
	}

	public static string ResolvePrivateKeyPemPath(string sharedServicesRootPath)
	{
		return Path.Combine(ResolveCertificatesDirectoryPath(sharedServicesRootPath), PrivateKeyPemFileName);
	}

	public Task<AcmeDnsCertificateState> LoadAsync(CancellationToken cancellationToken = default)
	{
		string accountKeyPath = ResolveAccountKeyPath(_sharedServicesRootPath);
		string orderedDomainNamePath = ResolveOrderedDomainNamePath(_sharedServicesRootPath);
		string certificatePemPath = ResolveCertificatePemPath(_sharedServicesRootPath);
		string fullChainPemPath = ResolveFullChainPemPath(_sharedServicesRootPath);
		string privateKeyPemPath = ResolvePrivateKeyPemPath(_sharedServicesRootPath);

		if (!File.Exists(accountKeyPath) && !File.Exists(orderedDomainNamePath) && !File.Exists(certificatePemPath) && !File.Exists(fullChainPemPath) && !File.Exists(privateKeyPemPath))
			return Task.FromResult<AcmeDnsCertificateState>(null);

		AcmeDnsCertificateState state = new AcmeDnsCertificateState()
		{
			OrderedDomainName = File.Exists(orderedDomainNamePath) ? File.ReadAllText(orderedDomainNamePath) : null,
			AccountKeyPem = File.Exists(accountKeyPath) ? File.ReadAllText(accountKeyPath) : null,
			CertificatePem = File.Exists(certificatePemPath) ? File.ReadAllText(certificatePemPath) : null,
			FullChainPem = File.Exists(fullChainPemPath) ? File.ReadAllText(fullChainPemPath) : null,
			PrivateKeyPem = File.Exists(privateKeyPemPath) ? File.ReadAllText(privateKeyPemPath) : null
		};
		state.NotAfterUtc = AcmeDnsCertificateRenewalOrchestrator.TryGetNotAfterUtc(state.CertificatePem ?? state.FullChainPem);
		return Task.FromResult(state);
	}

	public Task SaveAsync(AcmeDnsCertificate certificate, CancellationToken cancellationToken = default)
	{
		if (certificate == null)
			throw new ArgumentNullException(nameof(certificate));

		string directoryPath = ResolveCertificatesDirectoryPath(_sharedServicesRootPath);
		Directory.CreateDirectory(directoryPath);

		WriteAllTextAtomic(Path.Combine(directoryPath, AccountKeyFileName), certificate.AccountKeyPem ?? string.Empty);
		WriteAllTextAtomic(Path.Combine(directoryPath, OrderedDomainNameFileName), certificate.OrderedDomainName ?? string.Empty);
		WriteAllTextAtomic(Path.Combine(directoryPath, CertificatePemFileName), certificate.CertificatePem ?? string.Empty);
		WriteAllTextAtomic(Path.Combine(directoryPath, FullChainPemFileName), certificate.FullChainPem ?? string.Empty);
		WriteAllTextAtomic(Path.Combine(directoryPath, PrivateKeyPemFileName), certificate.PrivateKeyPem ?? string.Empty);
		return Task.CompletedTask;
	}

	private static void WriteAllTextAtomic(string path, string content)
	{
		string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
		File.WriteAllText(tempPath, content ?? string.Empty);
		File.Move(tempPath, path, true);
	}
}