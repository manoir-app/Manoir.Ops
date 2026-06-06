using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public sealed class AdminUiDeploymentDiff
{
	public string PluginId { get; set; }

	public string PreviousVersion { get; set; }

	public string CurrentVersion { get; set; }

	public IReadOnlyList<string> AddedContributionIds { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> RemovedContributionIds { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> ChangedContributionIds { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> UnchangedContributionIds { get; set; } = Array.Empty<string>();
}