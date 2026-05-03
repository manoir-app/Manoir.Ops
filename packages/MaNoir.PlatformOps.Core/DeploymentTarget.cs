namespace MaNoir.PlatformOps.Core;

public sealed class DeploymentTarget
{
	public DeploymentTargetKind Kind { get; set; }

	public string EnvironmentName { get; set; }

	public string ScopeName { get; set; }

	public string WorkloadName { get; set; }
}
