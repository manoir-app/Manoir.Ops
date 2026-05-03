using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public static class DockerDeploymentPlanFactory
{
	private static readonly Regex EnvironmentVariableReferenceRegex = new Regex(@"\$\{(?<name>[A-Za-z0-9_]+)\}", RegexOptions.Compiled);

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
		plan.ResolvedSharedEnvironmentVariables = resolveSecretAsync == null
			? await PluginEnvironmentSecretsResolver.ResolveAsync(descriptor.EnvironmentVariables, cancellationToken)
			: await PluginEnvironmentSecretsResolver.ResolveAsync(descriptor.EnvironmentVariables, resolveSecretAsync, cancellationToken);
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
				Image = service.Image,
				BuildContext = service.BuildContext,
				ContainerName = service.ContainerName,
				RestartPolicy = service.RestartPolicy,
				ImagePullPolicy = DockerImagePullPolicy.Always,
				Ports = service.Ports,
				Volumes = service.Volumes,
				DependsOn = service.DependsOn,
				Environment = service.Environment,
				ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>()
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