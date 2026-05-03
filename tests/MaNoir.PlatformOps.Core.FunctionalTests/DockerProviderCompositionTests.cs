using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.Core.FunctionalTests;

[TestClass]
public sealed class DockerProviderCompositionTests
{
	[TestMethod]
	public void FirstSlice_ShouldNormalizeDockerTargetEndToEnd()
	{
		PlatformOpsKernel kernel = new PlatformOpsKernel();
		kernel.RegisterNormalizer(DeploymentTargetKind.Docker, DockerDeploymentTargetNormalizer.Normalize);

		DeploymentTarget normalizedTarget = kernel.NormalizeTarget(new DeploymentTarget()
		{
			Kind = DeploymentTargetKind.Docker,
			EnvironmentName = null,
			ScopeName = null,
			WorkloadName = " Platform Ops Agent "
		});

		Assert.AreEqual(DeploymentTargetKind.Docker, normalizedTarget.Kind);
		Assert.AreEqual("local", normalizedTarget.EnvironmentName);
		Assert.AreEqual("local", normalizedTarget.ScopeName);
		Assert.AreEqual("platform-ops-agent", normalizedTarget.WorkloadName);
	}
}