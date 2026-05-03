using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public sealed class PluginManifest
{
	public string ApiVersion { get; set; }

	public string Kind { get; set; }

	public PluginManifestPlugin Plugin { get; set; }

	public PluginManifestDocumentation Documentation { get; set; }

	public PluginManifestDependencies Dependencies { get; set; }

	public PluginManifestCatalog Catalog { get; set; }

	public PluginManifestDeployment Deployment { get; set; }
}

public sealed class PluginManifestPlugin
{
	public string PluginId { get; set; }

	public string RepoUrl { get; set; }

	public string DisplayName { get; set; }

	public string Publisher { get; set; }

	public string Version { get; set; }

	public string MinimumMaNoirVersion { get; set; }
}

public sealed class PluginManifestDocumentation
{
	public Dictionary<string, string> OverviewPath { get; set; }

	public Dictionary<string, string> SetupPath { get; set; }
}

public sealed class PluginManifestDependencies
{
	public List<PluginManifestRequiredPlugin> RequiredPlugins { get; set; }
}

public sealed class PluginManifestRequiredPlugin
{
	public string PluginId { get; set; }

	public string RepoUrl { get; set; }

	public string MinVersion { get; set; }

	public bool Optional { get; set; }

	public Dictionary<string, string> Reason { get; set; }
}

public sealed class PluginManifestCatalog
{
	public List<PluginManifestAccessZone> AccessZones { get; set; }

	public List<PluginManifestContribution> Contributions { get; set; }
}

public sealed class PluginManifestAccessZone
{
	public string Id { get; set; }

	public Dictionary<string, string> Label { get; set; }

	public Dictionary<string, string> Description { get; set; }
}

public sealed class PluginManifestContribution
{
	public string Id { get; set; }

	public string Kind { get; set; }

	public Dictionary<string, string> Label { get; set; }

	public Dictionary<string, string> Description { get; set; }

	public bool CanCreateInstances { get; set; }

	public bool CanInstallMultipleTimes { get; set; }

	public List<string> Tags { get; set; }

	public PluginManifestAdminUiContribution AdminUi { get; set; }

	public PluginManifestIntegrationContribution Integration { get; set; }
}

public sealed class PluginManifestAdminUiContribution
{
	public string Domain { get; set; }

	public string AccessZoneId { get; set; }

	public string RequiredAccessLevel { get; set; }

	public List<PluginManifestAdminUiPage> Pages { get; set; }
}

public sealed class PluginManifestAdminUiPage
{
	public string Category { get; set; }

	public string Name { get; set; }

	public string Url { get; set; }

	public Dictionary<string, string> Labels { get; set; }
}

public sealed class PluginManifestIntegrationContribution
{
	public string Domain { get; set; }

	public string Category { get; set; }

	public string ServiceDependencyKind { get; set; }

	public bool RequiresExternalSubscription { get; set; }

	public string DocumentationUrl { get; set; }

	public List<PluginManifestPublishedEntityKind> PublishedEntityKinds { get; set; }
}

public sealed class PluginManifestPublishedEntityKind
{
	public string Kind { get; set; }

	public Dictionary<string, string> Descriptions { get; set; }
}

public sealed class PluginManifestDeployment
{
	public string Group { get; set; }

	public List<PluginManifestArtifact> Artifacts { get; set; }
}

public sealed class PluginManifestArtifact
{
	public string Kind { get; set; }

	public string Path { get; set; }
}