using System;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Kubernetes;

public static class KubernetesDeploymentTargetNormalizer
{
	public static DeploymentTarget Normalize(DeploymentTarget target)
	{
		if (target == null)
			throw new ArgumentNullException(nameof(target));

		if (target.Kind != DeploymentTargetKind.Kubernetes)
			throw new ArgumentException("The target kind must be Kubernetes.", nameof(target));

		return new DeploymentTarget()
		{
			Kind = DeploymentTargetKind.Kubernetes,
			EnvironmentName = PlatformOpsNaming.RequireSegment(target.EnvironmentName, "local", nameof(target.EnvironmentName)),
			ScopeName = PlatformOpsNaming.RequireSegment(target.ScopeName, "default", nameof(target.ScopeName)),
			WorkloadName = PlatformOpsNaming.RequireSegment(target.WorkloadName, "platformops", nameof(target.WorkloadName))
		};
	}
}
