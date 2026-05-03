using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class DockerDeploymentTargetNormalizerTests
{
	[TestMethod]
	public void Normalize_ShouldUseDockerDefaultsAndNormalizeSegments()
	{
		DeploymentTarget normalizedTarget = DockerDeploymentTargetNormalizer.Normalize(new DeploymentTarget()
		{
			Kind = DeploymentTargetKind.Docker,
			EnvironmentName = string.Empty,
			ScopeName = " Dev Host ",
			WorkloadName = " API Gateway "
		});

		Assert.AreEqual(DeploymentTargetKind.Docker, normalizedTarget.Kind);
		Assert.AreEqual("local", normalizedTarget.EnvironmentName);
		Assert.AreEqual("dev-host", normalizedTarget.ScopeName);
		Assert.AreEqual("api-gateway", normalizedTarget.WorkloadName);
	}
}
