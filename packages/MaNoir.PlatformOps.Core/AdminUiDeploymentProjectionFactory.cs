using System;
using System.Collections.Generic;
using System.Linq;

namespace MaNoir.PlatformOps.Core;

public static class AdminUiDeploymentProjectionFactory
{
	public static AdminUiDeploymentProjection Create(PluginManifest manifest, PluginDeploymentDescriptor descriptor)
	{
		if (manifest == null)
			throw new ArgumentNullException(nameof(manifest));

		if (descriptor == null)
			throw new ArgumentNullException(nameof(descriptor));

		if (!string.Equals(manifest.Plugin?.PluginId, descriptor.PluginId, StringComparison.Ordinal))
			throw new ArgumentException("The manifest and deployment descriptor must target the same plugin.", nameof(descriptor));

		List<AdminUiContributionDeploymentProjection> contributions = manifest.Catalog?.Contributions?
			.Where(contribution => contribution != null && string.Equals(contribution.Kind, "AdminUiPage", StringComparison.Ordinal) && contribution.AdminUi != null)
			.Select(contribution => new AdminUiContributionDeploymentProjection()
			{
				ContributionId = contribution.Id,
				Label = contribution.Label?.Values.FirstOrDefault(),
				Domain = contribution.AdminUi.Domain,
				AccessZoneId = contribution.AdminUi.AccessZoneId,
				RequiredAccessLevel = contribution.AdminUi.RequiredAccessLevel,
				Pages = (contribution.AdminUi.Pages ?? new List<PluginManifestAdminUiPage>())
					.Where(page => page != null)
					.Select(page => new AdminUiPageDeploymentProjection()
					{
						Category = page.Category,
						Name = page.Name,
						RelativePath = NormalizeRelativePath(page.RelativePath),
						EffectivePath = ResolveEffectivePath(descriptor.AdminUiPathPrefix, page.RelativePath, page.Url),
						LegacyUrl = string.IsNullOrWhiteSpace(page.Url) ? null : page.Url
					})
					.ToArray()
			})
			.OrderBy(contribution => contribution.ContributionId, StringComparer.Ordinal)
			.ToList()
			?? new List<AdminUiContributionDeploymentProjection>();

		return new AdminUiDeploymentProjection()
		{
			PluginId = descriptor.PluginId,
			PluginVersion = descriptor.Version,
			PluginDisplayName = descriptor.DisplayName,
			PublicBasePath = descriptor.AdminUiPathPrefix,
			ServiceName = descriptor.AdminUiServiceName,
			ServicePort = descriptor.AdminUiServicePort,
			Contributions = contributions
		};
	}

	private static string ResolveEffectivePath(string publicBasePath, string relativePath, string legacyUrl)
	{
		string normalizedRelativePath = NormalizeRelativePath(relativePath);
		if (normalizedRelativePath != null)
		{
			if (string.IsNullOrWhiteSpace(publicBasePath))
				return normalizedRelativePath;

			return CombinePaths(publicBasePath, normalizedRelativePath);
		}

		return string.IsNullOrWhiteSpace(legacyUrl) ? null : legacyUrl;
	}

	private static string NormalizeRelativePath(string relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath))
			return null;

		string trimmedPath = relativePath.Trim();
		return trimmedPath.StartsWith("/", StringComparison.Ordinal) ? trimmedPath : "/" + trimmedPath;
	}

	private static string CombinePaths(string publicBasePath, string relativePath)
	{
		string normalizedBasePath = string.IsNullOrWhiteSpace(publicBasePath) ? string.Empty : publicBasePath.Trim();
		if (normalizedBasePath.Length == 0 || string.Equals(normalizedBasePath, "/", StringComparison.Ordinal))
			return relativePath;

		string trimmedBasePath = normalizedBasePath.TrimEnd('/');
		return trimmedBasePath + relativePath;
	}
}