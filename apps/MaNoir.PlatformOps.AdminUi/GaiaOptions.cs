namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaOptions
{
	public string SharedServicesRootPath { get; set; }

	public string PluginRepositoriesRootPath { get; set; }

	public bool AutoEnsureSharedServicesOnStartup { get; set; } = true;

	public int EnsureIntervalSeconds { get; set; } = 300;
}