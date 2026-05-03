using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;
using MaNoir.PlatformOps.Provider.Kubernetes;

namespace MaNoir.PlatformOps.Core.FunctionalTests;

[TestClass]
public sealed class KubernetesProviderCompositionTests
{
	[TestMethod]
	public void FirstSlice_ShouldNormalizeKubernetesTargetEndToEnd()
	{
		PlatformOpsKernel kernel = new PlatformOpsKernel();
		kernel.RegisterNormalizer(DeploymentTargetKind.Kubernetes, KubernetesDeploymentTargetNormalizer.Normalize);

		DeploymentTarget normalizedTarget = kernel.NormalizeTarget(new DeploymentTarget()
		{
			Kind = DeploymentTargetKind.Kubernetes,
			EnvironmentName = string.Empty,
			ScopeName = string.Empty,
			WorkloadName = " Platform Ops Control Plane "
		});

		Assert.AreEqual(DeploymentTargetKind.Kubernetes, normalizedTarget.Kind);
		Assert.AreEqual("local", normalizedTarget.EnvironmentName);
		Assert.AreEqual("default", normalizedTarget.ScopeName);
		Assert.AreEqual("platform-ops-control-plane", normalizedTarget.WorkloadName);
	}
}
