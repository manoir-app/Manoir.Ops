namespace MaNoir.PlatformOps.AdminUi;

using System.Collections.Generic;

public sealed class GaiaOptions
{
	public string SharedServicesRootPath { get; set; }

	public string PluginRepositoriesRootPath { get; set; }

	public string RuntimeStatePath { get; set; }

	public List<string> RequiredPluginIds { get; set; } = [];

	public bool AutoEnsureSharedServicesOnStartup { get; set; } = true;

	public int EnsureIntervalSeconds { get; set; } = 300;
}