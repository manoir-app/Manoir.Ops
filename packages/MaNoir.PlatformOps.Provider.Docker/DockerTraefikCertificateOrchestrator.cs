using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certes.Acme;
using Docker.DotNet;
using Docker.DotNet.Models;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public sealed class DockerTraefikCertificateConfiguration
{
	public string DomainName { get; set; }

	public bool UseWildcard { get; set; } = true;

	public string AccountEmail { get; set; }

	public Uri AcmeServerUri { get; set; }

	public TimeSpan RenewBefore { get; set; } = TimeSpan.FromDays(5);

	public string EdgeContainerName { get; set; }

	public string ReloadSignal { get; set; }

	public OvhDnsChallengeSecretConfiguration OvhDns { get; set; }

	public string OrganizationUnit { get; set; } = "PlatformOps";
}

public sealed class DockerTraefikCertificateDeploymentResult
{
	public AcmeDnsCertificateRenewalResult Certificate { get; set; }

	public bool ReloadAttempted { get; set; }

	public bool ReloadPerformed { get; set; }

	public string ReloadedContainerName { get; set; }

	public string ReloadSignal { get; set; }
}

public sealed class DockerTraefikCertificateOrchestrator : IDisposable
{
	public const string EdgeContainerNameEnvironmentVariableName = "MANOIR_EDGE_TRAEFIK_CONTAINER_NAME";

	public const string ReloadSignalEnvironmentVariableName = "MANOIR_EDGE_TRAEFIK_RELOAD_SIGNAL";

	public const string DefaultEdgeContainerName = "manoir-edge-traefik";

	public const string DefaultReloadSignal = "HUP";

	private readonly string _sharedServicesRootPath;
	private readonly Func<string, CancellationToken, Task<string>> _resolveSecretAsync;
	private readonly Func<AcmeDnsCertificateRequest, AcmeDnsCertificateIssuerOptions, IAcmeDnsTxtChallengeWriter, CancellationToken, Task<AcmeDnsCertificate>> _issueAsync;
	private readonly Func<string, string, bool, CancellationToken, Task<bool>> _reloadAsync;
	private readonly Func<IAcmeDnsCertificateStateStore> _createStateStore;
	private readonly bool _ownsDockerClient;
	private readonly DockerClient _dockerClient;

	public DockerTraefikCertificateOrchestrator(string sharedServicesRootPath = null)
		: this(new DockerClientConfiguration().CreateClient(), sharedServicesRootPath, null, null, null, null)
	{
		_ownsDockerClient = true;
	}

	public DockerTraefikCertificateOrchestrator(
		DockerClient dockerClient,
		string sharedServicesRootPath,
		Func<string, CancellationToken, Task<string>> resolveSecretAsync,
		Func<AcmeDnsCertificateRequest, AcmeDnsCertificateIssuerOptions, IAcmeDnsTxtChallengeWriter, CancellationToken, Task<AcmeDnsCertificate>> issueAsync,
		Func<string, string, bool, CancellationToken, Task<bool>> reloadAsync,
		Func<IAcmeDnsCertificateStateStore> createStateStore)
	{
		_dockerClient = dockerClient;
		_sharedServicesRootPath = DockerSharedServicesCatalog.ResolveSharedServicesRootPath(sharedServicesRootPath);
		_resolveSecretAsync = resolveSecretAsync;
		_issueAsync = issueAsync ?? AcmeDnsCertificateIssuer.IssueAsync;
		_reloadAsync = reloadAsync ?? ReloadContainerAsync;
		_createStateStore = createStateStore ?? (() => new DockerTraefikCertificateStore(_sharedServicesRootPath));
	}

	public async Task<DockerTraefikCertificateDeploymentResult> EnsureCurrentAsync(DockerTraefikCertificateConfiguration configuration, CancellationToken cancellationToken = default)
	{
		if (configuration == null)
			throw new ArgumentNullException(nameof(configuration));

		if (configuration.OvhDns == null)
			throw new ArgumentException("An OVH DNS configuration is required.", nameof(configuration));

		OvhDnsChallengeCredentials credentials = _resolveSecretAsync == null
			? await OvhDnsChallengeCredentialsResolver.ResolveAsync(configuration.OvhDns, cancellationToken)
			: await OvhDnsChallengeCredentialsResolver.ResolveAsync(configuration.OvhDns, _resolveSecretAsync, cancellationToken);

		IAcmeDnsCertificateStateStore stateStore = _createStateStore();
		using OvhDnsTxtChallengeWriter dnsWriter = new OvhDnsTxtChallengeWriter(credentials);

		AcmeDnsCertificateRenewalOrchestrator renewalOrchestrator = new AcmeDnsCertificateRenewalOrchestrator(stateStore, _issueAsync);
		AcmeDnsCertificateRenewalResult renewalResult = await renewalOrchestrator.EnsureCurrentAsync(
			new AcmeDnsCertificateRequest()
			{
				DomainName = configuration.DomainName,
				UseWildcard = configuration.UseWildcard,
				OrganizationUnit = configuration.OrganizationUnit
			},
			new AcmeDnsCertificateIssuerOptions()
			{
				AccountEmail = configuration.AccountEmail,
				ServerUri = configuration.AcmeServerUri ?? WellKnownServers.LetsEncryptV2
			},
			dnsWriter,
			new AcmeDnsCertificateRenewalPolicy()
			{
				RenewBefore = configuration.RenewBefore
			},
			cancellationToken);

		string edgeContainerName = ResolveEdgeContainerName(configuration.EdgeContainerName);
		string reloadSignal = ResolveReloadSignal(configuration.ReloadSignal);
		bool reloadPerformed = false;

		if (renewalResult.WasRenewed)
			reloadPerformed = await _reloadAsync(edgeContainerName, reloadSignal, true, cancellationToken);

		return new DockerTraefikCertificateDeploymentResult()
		{
			Certificate = renewalResult,
			ReloadAttempted = renewalResult.WasRenewed,
			ReloadPerformed = reloadPerformed,
			ReloadedContainerName = edgeContainerName,
			ReloadSignal = reloadSignal
		};
	}

	public void Dispose()
	{
		if (_ownsDockerClient)
			_dockerClient?.Dispose();
	}

	private static string ResolveEdgeContainerName(string configuredContainerName)
	{
		string effectiveValue = string.IsNullOrWhiteSpace(configuredContainerName)
			? Environment.GetEnvironmentVariable(EdgeContainerNameEnvironmentVariableName)
			: configuredContainerName;

		return string.IsNullOrWhiteSpace(effectiveValue)
			? DefaultEdgeContainerName
			: effectiveValue.Trim();
	}

	private static string ResolveReloadSignal(string configuredReloadSignal)
	{
		string effectiveValue = string.IsNullOrWhiteSpace(configuredReloadSignal)
			? Environment.GetEnvironmentVariable(ReloadSignalEnvironmentVariableName)
			: configuredReloadSignal;

		return string.IsNullOrWhiteSpace(effectiveValue)
			? DefaultReloadSignal
			: effectiveValue.Trim();
	}

	private async Task<bool> ReloadContainerAsync(string containerName, string signal, bool fallbackToRestart, CancellationToken cancellationToken)
	{
		if (_dockerClient == null)
			throw new InvalidOperationException("A Docker client is required to reload the edge container.");

		IList<ContainerListResponse> containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters() { All = true }, cancellationToken);
		ContainerListResponse container = containers.FirstOrDefault(candidate => candidate?.Names != null && candidate.Names.Any(name => string.Equals(name?.TrimStart('/'), containerName, StringComparison.OrdinalIgnoreCase)));
		if (container == null || !string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase))
			return false;

		if (!string.IsNullOrWhiteSpace(signal))
		{
			try
			{
				await _dockerClient.Containers.KillContainerAsync(container.ID, new ContainerKillParameters() { Signal = signal }, cancellationToken);
				return true;
			}
			catch (DockerApiException) when (fallbackToRestart)
			{
			}
		}

		if (!fallbackToRestart)
			return false;

		await _dockerClient.Containers.RestartContainerAsync(container.ID, new ContainerRestartParameters(), cancellationToken);
		return true;
	}
}