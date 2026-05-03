using System;

namespace MaNoir.PlatformOps.Provider.Docker;

public static class DockerPlatformRuntimeEnvironment
{
	public const string DevelopmentInstanceEnvironmentVariableName = "MANOIR_DEVELOPMENT_INSTANCE";

	public static bool IsDevelopmentInstance()
	{
		string value = Environment.GetEnvironmentVariable(DevelopmentInstanceEnvironmentVariableName);
		return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
	}
}