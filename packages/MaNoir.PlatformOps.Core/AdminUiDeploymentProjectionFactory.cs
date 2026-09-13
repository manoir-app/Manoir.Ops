using System;

namespace MaNoir.PlatformOps.Core;

public static class AdminUiDeploymentProjectionFactory
{
	public static AdminUiDeploymentProjection Create(PluginDeploymentDescriptor descriptor)
	{
		if (descriptor == null)
			throw new ArgumentNullException(nameof(descriptor));

		return new AdminUiDeploymentProjection()
		{
			PluginId = descriptor.PluginId,
			PluginVersion = descriptor.Version,
			PluginDisplayName = descriptor.DisplayName,
			PublicBasePath = descriptor.AdminUiPathPrefix,
			ServiceName = descriptor.AdminUiServiceName,
			ServicePort = descriptor.AdminUiServicePort,
			Contributions = Array.Empty<AdminUiContributionDeploymentProjection>()
		};
	}
}