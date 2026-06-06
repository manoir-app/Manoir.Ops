using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class DockerClientFactoryTests
{
	[TestMethod]
	public void ResolveDefaultTimeout_ShouldUseDefaultWhenEnvironmentVariableIsMissing()
	{
		using EnvironmentVariableScope timeoutScope = new EnvironmentVariableScope(DockerClientFactory.DockerClientTimeoutSecondsEnvironmentVariableName, null);

		TimeSpan timeout = DockerClientFactory.ResolveDefaultTimeout();

		Assert.AreEqual(TimeSpan.FromSeconds(DockerClientFactory.DefaultDockerClientTimeoutSeconds), timeout);
	}

	[TestMethod]
	public void ResolveDefaultTimeout_ShouldUseConfiguredEnvironmentVariable()
	{
		using EnvironmentVariableScope timeoutScope = new EnvironmentVariableScope(DockerClientFactory.DockerClientTimeoutSecondsEnvironmentVariableName, "1800");

		TimeSpan timeout = DockerClientFactory.ResolveDefaultTimeout();

		Assert.AreEqual(TimeSpan.FromSeconds(1800), timeout);
	}

	[TestMethod]
	public void ResolveDefaultTimeout_ShouldRejectInvalidEnvironmentVariable()
	{
		using EnvironmentVariableScope timeoutScope = new EnvironmentVariableScope(DockerClientFactory.DockerClientTimeoutSecondsEnvironmentVariableName, "abc");

		InvalidOperationException exception = Assert.ThrowsException<InvalidOperationException>(() => DockerClientFactory.ResolveDefaultTimeout());

		Assert.AreEqual(DockerClientFactory.DockerClientTimeoutSecondsEnvironmentVariableName + " must contain a positive integer number of seconds.", exception.Message);
	}
}