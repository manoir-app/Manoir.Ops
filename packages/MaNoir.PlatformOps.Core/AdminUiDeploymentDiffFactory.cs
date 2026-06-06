using System;
using System.Collections.Generic;
using System.Linq;

namespace MaNoir.PlatformOps.Core;

public static class AdminUiDeploymentDiffFactory
{
	public static AdminUiDeploymentDiff Create(AdminUiDeploymentProjection previous, AdminUiDeploymentProjection current)
	{
		if (previous == null && current == null)
			throw new ArgumentException("A previous or current projection is required.");

		if (previous != null && current != null && !string.Equals(previous.PluginId, current.PluginId, StringComparison.Ordinal))
			throw new ArgumentException("The compared projections must target the same plugin.", nameof(current));

		Dictionary<string, AdminUiContributionDeploymentProjection> previousById = (previous?.Contributions ?? Array.Empty<AdminUiContributionDeploymentProjection>())
			.Where(contribution => !string.IsNullOrWhiteSpace(contribution?.ContributionId))
			.ToDictionary(contribution => contribution.ContributionId, StringComparer.Ordinal);

		Dictionary<string, AdminUiContributionDeploymentProjection> currentById = (current?.Contributions ?? Array.Empty<AdminUiContributionDeploymentProjection>())
			.Where(contribution => !string.IsNullOrWhiteSpace(contribution?.ContributionId))
			.ToDictionary(contribution => contribution.ContributionId, StringComparer.Ordinal);

		List<string> added = currentById.Keys.Where(id => !previousById.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
		List<string> removed = previousById.Keys.Where(id => !currentById.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
		List<string> changed = currentById.Keys
			.Where(previousById.ContainsKey)
			.Where(id => !AreEquivalent(previousById[id], currentById[id]))
			.OrderBy(id => id, StringComparer.Ordinal)
			.ToList();
		List<string> unchanged = currentById.Keys
			.Where(previousById.ContainsKey)
			.Where(id => AreEquivalent(previousById[id], currentById[id]))
			.OrderBy(id => id, StringComparer.Ordinal)
			.ToList();

		return new AdminUiDeploymentDiff()
		{
			PluginId = current?.PluginId ?? previous?.PluginId,
			PreviousVersion = previous?.PluginVersion,
			CurrentVersion = current?.PluginVersion,
			AddedContributionIds = added,
			RemovedContributionIds = removed,
			ChangedContributionIds = changed,
			UnchangedContributionIds = unchanged
		};
	}

	private static bool AreEquivalent(AdminUiContributionDeploymentProjection previous, AdminUiContributionDeploymentProjection current)
	{
		if (!string.Equals(previous?.Label, current?.Label, StringComparison.Ordinal))
			return false;

		if (!string.Equals(previous?.Domain, current?.Domain, StringComparison.Ordinal))
			return false;

		if (!string.Equals(previous?.AccessZoneId, current?.AccessZoneId, StringComparison.Ordinal))
			return false;

		if (!string.Equals(previous?.RequiredAccessLevel, current?.RequiredAccessLevel, StringComparison.Ordinal))
			return false;

		IReadOnlyList<AdminUiPageDeploymentProjection> previousPages = previous?.Pages ?? Array.Empty<AdminUiPageDeploymentProjection>();
		IReadOnlyList<AdminUiPageDeploymentProjection> currentPages = current?.Pages ?? Array.Empty<AdminUiPageDeploymentProjection>();
		if (previousPages.Count != currentPages.Count)
			return false;

		for (int index = 0; index < previousPages.Count; index++)
		{
			AdminUiPageDeploymentProjection previousPage = previousPages[index];
			AdminUiPageDeploymentProjection currentPage = currentPages[index];

			if (!string.Equals(previousPage?.Category, currentPage?.Category, StringComparison.Ordinal)
				|| !string.Equals(previousPage?.Name, currentPage?.Name, StringComparison.Ordinal)
				|| !string.Equals(previousPage?.RelativePath, currentPage?.RelativePath, StringComparison.Ordinal)
				|| !string.Equals(previousPage?.EffectivePath, currentPage?.EffectivePath, StringComparison.Ordinal)
				|| !string.Equals(previousPage?.LegacyUrl, currentPage?.LegacyUrl, StringComparison.Ordinal))
			{
				return false;
			}
		}

		return true;
	}
}