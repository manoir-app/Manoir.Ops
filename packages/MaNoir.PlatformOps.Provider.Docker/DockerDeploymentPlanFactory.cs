using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public static class DockerDeploymentPlanFactory
{
	private static readonly Regex EnvironmentVariableReferenceRegex = new Regex(@"\$\{(?<name>[A-Za-z0-9_]+)\}", RegexOptions.Compiled);
	private static readonly Regex TraefikResourceNameRegex = new Regex(@"[^a-z0-9-]+", RegexOptions.Compiled);

	public static DockerAdminUiRoutePlan CreateAdminUiRoutePlan(PluginDeploymentDescriptor descriptor)
	{
		if (descriptor == null)
			throw new ArgumentNullException(nameof(descriptor));

		if (string.IsNullOrWhiteSpace(descriptor.AdminUiPathPrefix)
			|| !descriptor.AdminUiServicePort.HasValue
			|| string.IsNullOrWhiteSpace(descriptor.AdminUiServiceName))
		{
			return null;
		}

		string pathPrefix = descriptor.AdminUiPathPrefix.Trim();
		string composeServiceName = descriptor.AdminUiServiceName.Trim();
		string traefikResourceName = CreateTraefikResourceName(descriptor.PluginId, composeServiceName, "admin-ui");
		string routerRule = $"PathPrefix(`{pathPrefix}`)";

		return new DockerAdminUiRoutePlan()
		{
			PluginId = descriptor.PluginId,
			PublicBasePath = pathPrefix,
			ComposeServiceName = composeServiceName,
			ServicePort = descriptor.AdminUiServicePort.Value,
			TraefikResourceName = traefikResourceName,
			RouterRule = routerRule,
			Labels = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["traefik.enable"] = "true",
				["traefik.docker.network"] = DockerRuntimeSpecFactory.SharedNetworkName,
				[$"traefik.http.routers.{traefikResourceName}.rule"] = routerRule,
				[$"traefik.http.routers.{traefikResourceName}.service"] = traefikResourceName,
				[$"traefik.http.services.{traefikResourceName}.loadbalancer.server.port"] = descriptor.AdminUiServicePort.Value.ToString(CultureInfo.InvariantCulture)
			}
		};
	}

	public static DockerDeploymentPlan Create(PluginDeploymentDescriptor descriptor)
	{
		PlatformOpsSecretsRuntimeGuard.EnsureConfigured();

		if (descriptor == null)
			throw new ArgumentNullException(nameof(descriptor));

		if (string.IsNullOrWhiteSpace(descriptor.ComposeArtifactFullPath))
			throw new ArgumentException("The deployment descriptor does not declare a resolved Docker Compose artifact path.", nameof(descriptor));

		DockerComposeFile composeFile = DockerComposeParser.ParseFile(descriptor.ComposeArtifactFullPath);
		return Create(descriptor, composeFile);
	}

	public static async Task<DockerDeploymentPlan> CreateAsync(PluginDeploymentDescriptor descriptor, CancellationToken cancellationToken = default)
	{
		PlatformOpsSecretsRuntimeGuard.EnsureConfigured();

		if (descriptor == null)
			throw new ArgumentNullException(nameof(descriptor));

		if (string.IsNullOrWhiteSpace(descriptor.ComposeArtifactFullPath))
			throw new ArgumentException("The deployment descriptor does not declare a resolved Docker Compose artifact path.", nameof(descriptor));

		DockerComposeFile composeFile = DockerComposeParser.ParseFile(descriptor.ComposeArtifactFullPath);
		return await CreateAsync(descriptor, composeFile, cancellationToken);
	}

	public static async Task<DockerDeploymentPlan> CreateAsync(PluginDeploymentDescriptor descriptor, DockerComposeFile composeFile, CancellationToken cancellationToken = default)
	{
		return await CreateAsync(descriptor, composeFile, null, cancellationToken);
	}

	public static async Task<DockerDeploymentPlan> CreateAsync(PluginDeploymentDescriptor descriptor, DockerComposeFile composeFile, Func<string, CancellationToken, Task<string>> resolveSecretAsync, CancellationToken cancellationToken = default)
	{
		PlatformOpsSecretsRuntimeGuard.EnsureConfigured();

		DockerDeploymentPlan plan = Create(descriptor, composeFile);
		if (descriptor.EnvironmentVariables == null || descriptor.EnvironmentVariables.Count == 0)
		{
			plan.ResolvedSharedEnvironmentVariables = Array.Empty<PluginResolvedEnvironmentVariable>();
		}
		else
		{
			plan.ResolvedSharedEnvironmentVariables = resolveSecretAsync == null
				? await PluginEnvironmentSecretsResolver.ResolveAsync(descriptor.EnvironmentVariables, cancellationToken)
				: await PluginEnvironmentSecretsResolver.ResolveAsync(descriptor.EnvironmentVariables, resolveSecretAsync, cancellationToken);
		}

		ApplyResolvedServiceEnvironment(plan);
		return plan;
	}

	public static DockerDeploymentPlan Create(PluginDeploymentDescriptor descriptor, DockerComposeFile composeFile)
	{
		PlatformOpsSecretsRuntimeGuard.EnsureConfigured();

		if (descriptor == null)
			throw new ArgumentNullException(nameof(descriptor));

		if (composeFile == null)
			throw new ArgumentNullException(nameof(composeFile));

		List<DockerDeploymentServicePlan> services = new List<DockerDeploymentServicePlan>();

		foreach (DockerComposeService service in composeFile.Services)
		{
			services.Add(new DockerDeploymentServicePlan()
			{
				Name = service.Name,
				Image = NormalizeImageForRuntime(service.Image),
				BuildContext = service.BuildContext,
				ContainerName = service.ContainerName,
				RestartPolicy = service.RestartPolicy,
				ImagePullPolicy = DockerImagePullPolicy.Always,
				Ports = service.Ports,
				Volumes = service.Volumes,
				DependsOn = service.DependsOn,
				Environment = service.Environment,
				ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>(),
				Labels = CreateServiceLabels(descriptor, service)
			});
		}

		return new DockerDeploymentPlan()
		{
			PluginId = descriptor.PluginId,
			DeploymentGroup = string.IsNullOrWhiteSpace(descriptor.DeploymentGroup) ? descriptor.PluginId : descriptor.DeploymentGroup,
			RepositoryRootPath = descriptor.RepositoryRootPath,
			ComposeFilePath = descriptor.ComposeArtifactFullPath,
			SharedEnvironmentVariables = descriptor.EnvironmentVariables,
			ResolvedSharedEnvironmentVariables = Array.Empty<PluginResolvedEnvironmentVariable>(),
			Services = services
		};
	}

	internal static string NormalizeImageForRuntime(string imageReference)
	{
		if (string.IsNullOrWhiteSpace(imageReference) || !DockerPlatformRuntimeEnvironment.IsDevelopmentInstance())
			return imageReference;

		if (imageReference.Contains('@', StringComparison.Ordinal))
			return imageReference;

		int lastSlashIndex = imageReference.LastIndexOf('/');
		int lastColonIndex = imageReference.LastIndexOf(':');
		if (lastColonIndex <= lastSlashIndex)
			return imageReference + ":dev";

		string tag = imageReference.Substring(lastColonIndex + 1);
		if (!string.Equals(tag, "latest", StringComparison.OrdinalIgnoreCase))
			return imageReference;

		return imageReference.Substring(0, lastColonIndex + 1) + "dev";
	}

	private static void ApplyResolvedServiceEnvironment(DockerDeploymentPlan plan)
	{
		Dictionary<string, string> resolvedVariablesByName = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, string> pair in DockerAutomaticEnvironmentVariables.CreateVariablesByName(plan.PluginId))
			resolvedVariablesByName[pair.Key] = pair.Value;

		foreach (PluginResolvedEnvironmentVariable variable in plan.ResolvedSharedEnvironmentVariables)
		{
			if (variable == null || string.IsNullOrWhiteSpace(variable.Name))
				continue;

			resolvedVariablesByName[variable.Name] = variable.Value;
		}

		List<string> errors = new List<string>();

		foreach (DockerDeploymentServicePlan service in plan.Services)
		{
			List<DockerResolvedEnvironmentEntry> resolvedEnvironment = new List<DockerResolvedEnvironmentEntry>();

			foreach (DockerComposeEnvironmentEntry entry in service.Environment)
			{
				if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
					continue;

				string resolvedValue = ResolveEnvironmentValue(service.Name, entry, resolvedVariablesByName, errors);
				if (resolvedValue == null)
					continue;

				resolvedEnvironment.Add(new DockerResolvedEnvironmentEntry()
				{
					Name = entry.Name,
					Value = resolvedValue
				});
			}

			service.ResolvedEnvironment = resolvedEnvironment;
		}

		if (errors.Count > 0)
			throw new DockerComposeEnvironmentResolutionException(errors);
	}

	private static IReadOnlyDictionary<string, string> CreateServiceLabels(PluginDeploymentDescriptor descriptor, DockerComposeService service)
	{
		DockerAdminUiRoutePlan routePlan = descriptor == null ? null : CreateAdminUiRoutePlan(descriptor);
		if (routePlan == null || service == null || !string.Equals(service.Name, routePlan.ComposeServiceName, StringComparison.OrdinalIgnoreCase))
			return new Dictionary<string, string>(StringComparer.Ordinal);

		return new Dictionary<string, string>(routePlan.Labels, StringComparer.Ordinal);
	}

	private static string CreateTraefikResourceName(string pluginId, string serviceName, string suffix)
	{
		string value = string.Join("-",
			new[] { pluginId, serviceName, suffix }
				.Where(part => !string.IsNullOrWhiteSpace(part)))
			.ToLowerInvariant();

		value = TraefikResourceNameRegex.Replace(value, "-").Trim('-');
		return string.IsNullOrWhiteSpace(value) ? "plugin-admin-ui" : value;
	}

	private static string ResolveEnvironmentValue(string serviceName, DockerComposeEnvironmentEntry entry, IReadOnlyDictionary<string, string> resolvedVariablesByName, List<string> errors)
	{
		if (entry.Value == null)
		{
			if (!resolvedVariablesByName.TryGetValue(entry.Name, out string value))
			{
				errors.Add($"services.{serviceName}.environment.{entry.Name} references a missing variable.");
				return null;
			}

			return value;
		}

		MatchCollection matches = EnvironmentVariableReferenceRegex.Matches(entry.Value);
		if (matches.Count == 0)
			return entry.Value;

		return EnvironmentVariableReferenceRegex.Replace(entry.Value, match =>
		{
			string variableName = match.Groups["name"].Value;
			if (!resolvedVariablesByName.TryGetValue(variableName, out string value))
			{
				errors.Add($"services.{serviceName}.environment.{entry.Name} references missing variable '{variableName}'.");
				return match.Value;
			}

			return value;
		});
	}
}