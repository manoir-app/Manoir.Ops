using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class DockerAutomaticEnvironmentVariablesTests
{
	[TestMethod]
	public void CreateResolvedEntries_ShouldInjectObservabilityVariablesByDefaultForPlugins()
	{
		using EnvironmentVariableScope observabilityScope = new EnvironmentVariableScope("MANOIR_OBSERVABILITY_ENABLED", null);
		using EnvironmentVariableScope pluginObservabilityScope = new EnvironmentVariableScope("MANOIR_PLUGIN_OBSERVABILITY_ENABLED", null);

		var entries = DockerAutomaticEnvironmentVariables.CreateResolvedEntries("erza");

		Assert.AreEqual("true", entries.Single(entry => entry.Name == "MANOIR_OBSERVABILITY_ENABLED").Value);
		Assert.AreEqual("http://tempo:4318/v1/traces", entries.Single(entry => entry.Name == "MANOIR_OTEL_TRACES_ENDPOINT").Value);
		Assert.AreEqual("http://loki:3100/otlp/v1/logs", entries.Single(entry => entry.Name == "MANOIR_OTEL_LOGS_ENDPOINT").Value);
	}

	[TestMethod]
	public void CreateResolvedEntries_ShouldAllowExplicitPluginObservabilityOptOut()
	{
		using EnvironmentVariableScope pluginObservabilityScope = new EnvironmentVariableScope("MANOIR_PLUGIN_OBSERVABILITY_ENABLED", "false");

		var entries = DockerAutomaticEnvironmentVariables.CreateResolvedEntries("erza");

		Assert.IsFalse(entries.Any(entry => entry.Name == "MANOIR_OBSERVABILITY_ENABLED"));
		Assert.IsFalse(entries.Any(entry => entry.Name == "MANOIR_OTEL_TRACES_ENDPOINT"));
		Assert.IsFalse(entries.Any(entry => entry.Name == "MANOIR_OTEL_LOGS_ENDPOINT"));
	}
}