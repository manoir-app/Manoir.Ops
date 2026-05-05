using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MaNoir.PlatformOps.Core;

public static class PluginManifestParser
{
	private static readonly HashSet<string> SupportedContributionKinds = new HashSet<string>(StringComparer.Ordinal)
	{
		"AdminUiPage",
		"Integration"
	};

	private static readonly HashSet<string> SupportedAccessLevels = new HashSet<string>(StringComparer.Ordinal)
	{
		"Read",
		"Write",
		"Admin"
	};

	private static readonly HashSet<string> SupportedArtifactKinds = new HashSet<string>(StringComparer.Ordinal)
	{
		"compose",
		"env-template"
	};

	public static PluginManifest Parse(string yamlText)
	{
		if (string.IsNullOrWhiteSpace(yamlText))
			throw new ArgumentException("A YAML manifest is required.", nameof(yamlText));

		DeserializerBuilder builder = new DeserializerBuilder()
			.WithNamingConvention(CamelCaseNamingConvention.Instance);

		try
		{
			PluginManifest manifest = builder.Build().Deserialize<PluginManifest>(new StringReader(yamlText));
			Validate(manifest);
			return manifest;
		}
		catch (YamlException exception)
		{
			throw new PluginManifestValidationException(new[] { "The manifest YAML could not be parsed." }, exception);
		}
	}

	public static PluginManifest ParseFile(string manifestPath)
	{
		if (string.IsNullOrWhiteSpace(manifestPath))
			throw new ArgumentException("A manifest path is required.", nameof(manifestPath));

		return Parse(File.ReadAllText(manifestPath));
	}

	private static void Validate(PluginManifest manifest)
	{
		List<string> errors = new List<string>();

		if (manifest == null)
		{
			errors.Add("The manifest document is empty.");
			throw new PluginManifestValidationException(errors);
		}

		if (!string.Equals(manifest.ApiVersion, "manoir/v1", StringComparison.Ordinal))
			errors.Add("apiVersion must be 'manoir/v1'.");

		if (!string.Equals(manifest.Kind, "PluginManifest", StringComparison.Ordinal))
			errors.Add("kind must be 'PluginManifest'.");

		ValidatePlugin(manifest.Plugin, errors);
		ValidateDocumentation(manifest.Documentation, errors);
		ValidateDependencies(manifest.Dependencies, errors);
		ValidateCatalog(manifest.Catalog, errors);
		ValidateDeployment(manifest.Deployment, errors);

		if (errors.Count > 0)
			throw new PluginManifestValidationException(errors);
	}

	private static void ValidatePlugin(PluginManifestPlugin plugin, List<string> errors)
	{
		if (plugin == null)
		{
			errors.Add("plugin is required.");
			return;
		}

		RequireValue(plugin.PluginId, "plugin.pluginId", errors);
		RequireValue(plugin.RepoUrl, "plugin.repoUrl", errors);
		RequireValue(plugin.DisplayName, "plugin.displayName", errors);
		RequireValue(plugin.Publisher, "plugin.publisher", errors);
		RequireValue(plugin.Version, "plugin.version", errors);
		RequireValue(plugin.MinimumMaNoirVersion, "plugin.minimumMaNoirVersion", errors);
		RequireHttpUrl(plugin.RepoUrl, "plugin.repoUrl", errors);
	}

	private static void ValidateDocumentation(PluginManifestDocumentation documentation, List<string> errors)
	{
		if (documentation == null)
			return;

		ValidateLocalizedMap(documentation.OverviewPath, "documentation.overviewPath", errors);
		ValidateLocalizedMap(documentation.SetupPath, "documentation.setupPath", errors);
	}

	private static void ValidateDependencies(PluginManifestDependencies dependencies, List<string> errors)
	{
		if (dependencies == null || dependencies.RequiredPlugins == null)
			return;

		for (int index = 0; index < dependencies.RequiredPlugins.Count; index++)
		{
			PluginManifestRequiredPlugin dependency = dependencies.RequiredPlugins[index];
			string prefix = $"dependencies.requiredPlugins[{index}]";

			if (dependency == null)
			{
				errors.Add($"{prefix} is required.");
				continue;
			}

			RequireValue(dependency.PluginId, prefix + ".pluginId", errors);
			RequireValue(dependency.RepoUrl, prefix + ".repoUrl", errors);
			RequireValue(dependency.MinVersion, prefix + ".minVersion", errors);
			RequireHttpUrl(dependency.RepoUrl, prefix + ".repoUrl", errors);
			ValidateLocalizedMap(dependency.Reason, prefix + ".reason", errors);
		}
	}

	private static void ValidateCatalog(PluginManifestCatalog catalog, List<string> errors)
	{
		if (catalog == null)
			return;

		if (catalog.AccessZones != null)
		{
			for (int index = 0; index < catalog.AccessZones.Count; index++)
			{
				PluginManifestAccessZone accessZone = catalog.AccessZones[index];
				string prefix = $"catalog.accessZones[{index}]";

				if (accessZone == null)
				{
					errors.Add($"{prefix} is required.");
					continue;
				}

				RequireValue(accessZone.Id, prefix + ".id", errors);
				ValidateLocalizedMap(accessZone.Label, prefix + ".label", errors);
				ValidateLocalizedMap(accessZone.Description, prefix + ".description", errors);
			}
		}

		if (catalog.Contributions == null)
			return;

		for (int index = 0; index < catalog.Contributions.Count; index++)
		{
			PluginManifestContribution contribution = catalog.Contributions[index];
			string prefix = $"catalog.contributions[{index}]";

			if (contribution == null)
			{
				errors.Add($"{prefix} is required.");
				continue;
			}

			RequireValue(contribution.Id, prefix + ".id", errors);
			RequireValue(contribution.Kind, prefix + ".kind", errors);
			ValidateLocalizedMap(contribution.Label, prefix + ".label", errors);
			ValidateLocalizedMap(contribution.Description, prefix + ".description", errors);

			if (!string.IsNullOrWhiteSpace(contribution.Kind) && !SupportedContributionKinds.Contains(contribution.Kind))
				errors.Add(prefix + ".kind is not supported.");

			if (string.Equals(contribution.Kind, "AdminUiPage", StringComparison.Ordinal))
				ValidateAdminUiContribution(contribution.AdminUi, prefix + ".adminUi", errors);

			if (string.Equals(contribution.Kind, "Integration", StringComparison.Ordinal))
				ValidateIntegrationContribution(contribution.Integration, prefix + ".integration", errors);
		}
	}

	private static void ValidateAdminUiContribution(PluginManifestAdminUiContribution adminUi, string prefix, List<string> errors)
	{
		if (adminUi == null)
		{
			errors.Add(prefix + " is required for an AdminUiPage contribution.");
			return;
		}

		RequireValue(adminUi.Domain, prefix + ".domain", errors);
		RequireValue(adminUi.AccessZoneId, prefix + ".accessZoneId", errors);
		RequireValue(adminUi.RequiredAccessLevel, prefix + ".requiredAccessLevel", errors);

		if (!string.IsNullOrWhiteSpace(adminUi.RequiredAccessLevel) && !SupportedAccessLevels.Contains(adminUi.RequiredAccessLevel))
			errors.Add(prefix + ".requiredAccessLevel is not supported.");

		if (adminUi.Pages == null || adminUi.Pages.Count == 0)
		{
			errors.Add(prefix + ".pages must contain at least one page.");
			return;
		}

		for (int index = 0; index < adminUi.Pages.Count; index++)
		{
			PluginManifestAdminUiPage page = adminUi.Pages[index];
			string pagePrefix = $"{prefix}.pages[{index}]";

			if (page == null)
			{
				errors.Add(pagePrefix + " is required.");
				continue;
			}

			RequireValue(page.Category, pagePrefix + ".category", errors);
			RequireValue(page.Name, pagePrefix + ".name", errors);
			RequireValue(page.Url, pagePrefix + ".url", errors);
			ValidateLocalizedMap(page.Labels, pagePrefix + ".labels", errors);
		}
	}

	private static void ValidateIntegrationContribution(PluginManifestIntegrationContribution integration, string prefix, List<string> errors)
	{
		if (integration == null)
		{
			errors.Add(prefix + " is required for an Integration contribution.");
			return;
		}

		RequireValue(integration.Domain, prefix + ".domain", errors);
		RequireValue(integration.Category, prefix + ".category", errors);
		RequireValue(integration.ServiceDependencyKind, prefix + ".serviceDependencyKind", errors);
		RequireHttpUrl(integration.DocumentationUrl, prefix + ".documentationUrl", errors);

		if (integration.PublishedEntityKinds == null)
			return;

		for (int index = 0; index < integration.PublishedEntityKinds.Count; index++)
		{
			PluginManifestPublishedEntityKind publishedEntityKind = integration.PublishedEntityKinds[index];
			string entityPrefix = $"{prefix}.publishedEntityKinds[{index}]";

			if (publishedEntityKind == null)
			{
				errors.Add(entityPrefix + " is required.");
				continue;
			}

			RequireValue(publishedEntityKind.Kind, entityPrefix + ".kind", errors);
			ValidateLocalizedMap(publishedEntityKind.Descriptions, entityPrefix + ".descriptions", errors);
		}
	}

	private static void ValidateDeployment(PluginManifestDeployment deployment, List<string> errors)
	{
		if (deployment == null)
			return;

		if (deployment.Group != null && string.IsNullOrWhiteSpace(deployment.Group))
			errors.Add("deployment.group must not be empty when provided.");

		if (deployment.Artifacts == null)
			return;

		for (int index = 0; index < deployment.Artifacts.Count; index++)
		{
			PluginManifestArtifact artifact = deployment.Artifacts[index];
			string prefix = $"deployment.artifacts[{index}]";

			if (artifact == null)
			{
				errors.Add(prefix + " is required.");
				continue;
			}

			RequireValue(artifact.Kind, prefix + ".kind", errors);
			RequireValue(artifact.Path, prefix + ".path", errors);
			RequireRelativePath(artifact.Path, prefix + ".path", errors);

			if (!string.IsNullOrWhiteSpace(artifact.Kind) && !SupportedArtifactKinds.Contains(artifact.Kind))
				errors.Add(prefix + ".kind is not supported.");
		}
	}

	private static void ValidateLocalizedMap(Dictionary<string, string> values, string fieldName, List<string> errors)
	{
		if (values == null)
			return;

		if (values.Count == 0)
		{
			errors.Add(fieldName + " must contain at least one localized value.");
			return;
		}

		foreach (KeyValuePair<string, string> pair in values)
		{
			if (string.IsNullOrWhiteSpace(pair.Key))
				errors.Add(fieldName + " contains an empty locale key.");

			if (string.IsNullOrWhiteSpace(pair.Value))
				errors.Add(fieldName + " contains an empty localized value.");

			if (fieldName.EndsWith("Path", StringComparison.Ordinal) || fieldName.EndsWith("path", StringComparison.Ordinal))
				RequireRelativePath(pair.Value, fieldName + "[" + pair.Key + "]", errors);
		}
	}

	private static void RequireValue(string value, string fieldName, List<string> errors)
	{
		if (string.IsNullOrWhiteSpace(value))
			errors.Add(fieldName + " is required.");
	}

	private static void RequireHttpUrl(string value, string fieldName, List<string> errors)
	{
		if (string.IsNullOrWhiteSpace(value))
			return;

		if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			errors.Add(fieldName + " must be an absolute http or https URL.");
	}

	private static void RequireRelativePath(string value, string fieldName, List<string> errors)
	{
		if (string.IsNullOrWhiteSpace(value))
			return;

		if (IsAbsolutePath(value))
			errors.Add(fieldName + " must be a relative path.");
	}

	private static bool IsAbsolutePath(string value)
	{
		if (System.IO.Path.IsPathRooted(value))
			return true;

		if (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && (value[2] == '/' || value[2] == '\\'))
			return true;

		if (value.StartsWith("\\\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
			return true;

		return false;
	}
}