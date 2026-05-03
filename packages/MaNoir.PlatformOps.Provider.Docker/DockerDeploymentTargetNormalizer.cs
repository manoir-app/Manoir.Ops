using System;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public static class DockerDeploymentTargetNormalizer
{
	public static DeploymentTarget Normalize(DeploymentTarget target)
	{
		if (target == null)
			throw new ArgumentNullException(nameof(target));

		if (target.Kind != DeploymentTargetKind.Docker)
			throw new ArgumentException("The target kind must be Docker.", nameof(target));

		return new DeploymentTarget()
		{
			Kind = DeploymentTargetKind.Docker,
			EnvironmentName = PlatformOpsNaming.RequireSegment(target.EnvironmentName, "local", nameof(target.EnvironmentName)),
			ScopeName = PlatformOpsNaming.RequireSegment(target.ScopeName, "local", nameof(target.ScopeName)),
			WorkloadName = PlatformOpsNaming.RequireSegment(target.WorkloadName, "platformops", nameof(target.WorkloadName))
		};
	}
}
