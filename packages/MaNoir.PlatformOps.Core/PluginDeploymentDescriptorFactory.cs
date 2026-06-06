using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public static class PluginDeploymentDescriptorFactory
{
	public static PluginDeploymentDescriptor Create(PluginManifest manifest, PluginEnvironmentTemplate environmentTemplate = null)
	{
		if (manifest == null)
			throw new ArgumentNullException(nameof(manifest));

		if (manifest.Plugin == null)
			throw new ArgumentException("The manifest plugin section is required.", nameof(manifest));

		PluginManifestArtifact composeArtifact = null;
		PluginManifestArtifact environmentArtifact = null;
		List<string> errors = new List<string>();

		if (manifest.Deployment?.Artifacts != null)
		{
			for (int index = 0; index < manifest.Deployment.Artifacts.Count; index++)
			{
				PluginManifestArtifact artifact = manifest.Deployment.Artifacts[index];

				if (artifact == null)
					continue;

				if (string.Equals(artifact.Kind, "compose", StringComparison.Ordinal))
				{
					if (composeArtifact != null)
						errors.Add("deployment.artifacts must not contain more than one compose artifact.");

					composeArtifact = artifact;
				}

				if (string.Equals(artifact.Kind, "env-template", StringComparison.Ordinal))
				{
					if (environmentArtifact != null)
						errors.Add("deployment.artifacts must not contain more than one env-template artifact.");

					environmentArtifact = artifact;
				}
			}
		}

		if (errors.Count > 0)
			throw new PluginManifestValidationException(errors);

		if (environmentTemplate != null && environmentArtifact == null)
			throw new ArgumentException("An env template was provided, but the manifest does not declare an env-template artifact.", nameof(environmentTemplate));

		return new PluginDeploymentDescriptor()
		{
			PluginId = manifest.Plugin.PluginId,
			RepoUrl = manifest.Plugin.RepoUrl,
			DisplayName = manifest.Plugin.DisplayName,
			Version = manifest.Plugin.Version,
			MinimumMaNoirVersion = manifest.Plugin.MinimumMaNoirVersion,
			DeploymentGroup = manifest.Deployment?.Group,
			AdminUiPathPrefix = manifest.Deployment?.AdminUi?.PathPrefix,
			AdminUiServiceName = string.IsNullOrWhiteSpace(manifest.Deployment?.AdminUi?.ComposeService)
				? manifest.Deployment?.AdminUi?.Service
				: manifest.Deployment?.AdminUi?.ComposeService,
			AdminUiServicePort = manifest.Deployment?.AdminUi?.Port,
			ComposeArtifactPath = composeArtifact?.Path,
			EnvironmentTemplatePath = environmentArtifact?.Path,
			EnvironmentVariables = environmentTemplate?.Variables ?? Array.Empty<PluginEnvironmentVariable>()
		};
	}
}