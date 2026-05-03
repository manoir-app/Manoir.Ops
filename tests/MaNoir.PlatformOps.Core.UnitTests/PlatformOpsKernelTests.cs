using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;
using MaNoir.PlatformOps.Provider.Kubernetes;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class PlatformOpsKernelTests
{
	[TestMethod]
	public void NormalizeTarget_ShouldUseRegisteredNormalizer()
	{
		PlatformOpsKernel kernel = new PlatformOpsKernel();
		kernel.RegisterNormalizer(DeploymentTargetKind.Kubernetes, KubernetesDeploymentTargetNormalizer.Normalize);

		DeploymentTarget normalizedTarget = kernel.NormalizeTarget(new DeploymentTarget()
		{
			Kind = DeploymentTargetKind.Kubernetes,
			EnvironmentName = "Prod West",
			ScopeName = " Apps ",
			WorkloadName = " Catalog Api "
		});

		Assert.AreEqual("prod-west", normalizedTarget.EnvironmentName);
		Assert.AreEqual("apps", normalizedTarget.ScopeName);
		Assert.AreEqual("catalog-api", normalizedTarget.WorkloadName);
	}
}
