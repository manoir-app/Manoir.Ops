using System;
using System.Collections.Generic;
using Docker.DotNet;

namespace MaNoir.PlatformOps.Provider.Docker;

public static class DockerClientFactory
{
	public const string DockerClientTimeoutSecondsEnvironmentVariableName = "MANOIR_DOCKER_CLIENT_TIMEOUT_SECONDS";

	public const int DefaultDockerClientTimeoutSeconds = 300;

	public const int DefaultNamedPipeConnectTimeoutSeconds = 30;

	public static DockerClient CreateClient()
	{
		DockerClientConfiguration configuration = new DockerClientConfiguration(
			credentials: null,
			defaultTimeout: ResolveDefaultTimeout(),
			namedPipeConnectTimeout: ResolveNamedPipeConnectTimeout(),
			defaultHttpRequestHeaders: new Dictionary<string, string>(StringComparer.Ordinal));

		return configuration.CreateClient();
	}

	public static TimeSpan ResolveDefaultTimeout()
	{
		return TimeSpan.FromSeconds(ResolvePositiveIntegerEnvironmentVariable(
			DockerClientTimeoutSecondsEnvironmentVariableName,
			DefaultDockerClientTimeoutSeconds));
	}

	public static TimeSpan ResolveNamedPipeConnectTimeout()
	{
		return TimeSpan.FromSeconds(DefaultNamedPipeConnectTimeoutSeconds);
	}

	private static int ResolvePositiveIntegerEnvironmentVariable(string environmentVariableName, int defaultValue)
	{
		string rawValue = Environment.GetEnvironmentVariable(environmentVariableName);
		if (string.IsNullOrWhiteSpace(rawValue))
			return defaultValue;

		if (!int.TryParse(rawValue.Trim(), out int parsedValue) || parsedValue <= 0)
			throw new InvalidOperationException(environmentVariableName + " must contain a positive integer number of seconds.");

		return parsedValue;
	}
}