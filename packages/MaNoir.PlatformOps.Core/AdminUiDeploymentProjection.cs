using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public sealed class AdminUiDeploymentProjection
{
	public string PluginId { get; set; }

	public string PluginVersion { get; set; }

	public string PluginDisplayName { get; set; }

	public string PublicBasePath { get; set; }

	public string ServiceName { get; set; }

	public int? ServicePort { get; set; }

	public IReadOnlyList<AdminUiContributionDeploymentProjection> Contributions { get; set; } = Array.Empty<AdminUiContributionDeploymentProjection>();
}

public sealed class AdminUiContributionDeploymentProjection
{
	public string ContributionId { get; set; }

	public string Label { get; set; }

	public string Domain { get; set; }

	public string AccessZoneId { get; set; }

	public string RequiredAccessLevel { get; set; }

	public IReadOnlyList<AdminUiPageDeploymentProjection> Pages { get; set; } = Array.Empty<AdminUiPageDeploymentProjection>();
}

public sealed class AdminUiPageDeploymentProjection
{
	public string Category { get; set; }

	public string Name { get; set; }

	public string RelativePath { get; set; }

	public string EffectivePath { get; set; }

	public string LegacyUrl { get; set; }
}