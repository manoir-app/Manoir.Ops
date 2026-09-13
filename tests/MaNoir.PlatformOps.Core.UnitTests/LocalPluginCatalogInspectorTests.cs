using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class LocalPluginCatalogInspectorTests
{
	[TestMethod]
	public void EvaluateRequiredPlugins_ShouldReportMissingRequiredPluginWhenCatalogDoesNotContainIt()
	{
		string pluginCatalogRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

		try
		{
			Directory.CreateDirectory(pluginCatalogRootPath);

			RequiredPluginAvailabilityEvaluation evaluation = LocalPluginCatalogInspector.EvaluateRequiredPlugins(pluginCatalogRootPath, ["platform"]);

			Assert.AreEqual(1, evaluation.MissingRequiredPluginIds.Count);
			Assert.AreEqual("platform", evaluation.MissingRequiredPluginIds[0]);
			Assert.IsFalse(evaluation.HasAllRequiredPluginsAvailable);
		}
		finally
		{
			if (Directory.Exists(pluginCatalogRootPath))
				Directory.Delete(pluginCatalogRootPath, true);
		}
	}

	[TestMethod]
	public void EvaluateRequiredPlugins_ShouldDiscoverPluginIdFromLocalManifestDirectory()
	{
		string pluginCatalogRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string pluginRootPath = Path.Combine(pluginCatalogRootPath, "platform");

		try
		{
			Directory.CreateDirectory(Path.Combine(pluginRootPath, "deploy"));
			File.WriteAllText(Path.Combine(pluginRootPath, "plugin.yaml"),
				"apiVersion: manoir/v1\n"
				+ "kind: AvailablePlugin\n"
				+ "repoUrl: https://github.com/manoir-app/MaNoir.Platform\n"
				+ "displayName: Platform\n"
				+ "version: 1.0.0\n"
				+ "minimumMaNoirVersion: 1.0.0\n"
				+ "deployment:\n"
				+ "  group: platform\n"
				+ "  adminUi:\n"
				+ "    pathPrefix: /platform\n"
				+ "    composeService: core\n"
				+ "    port: 8080\n"
				+ "  artifacts:\n"
				+ "    - kind: compose\n"
				+ "      path: deploy/docker-compose.yml\n");
			File.WriteAllText(Path.Combine(pluginRootPath, "deploy", "docker-compose.yml"), "services:\n  core:\n    image: manoir/platform:1.0.0\n");

			RequiredPluginAvailabilityEvaluation evaluation = LocalPluginCatalogInspector.EvaluateRequiredPlugins(pluginCatalogRootPath, ["platform"]);

			Assert.AreEqual(1, evaluation.AvailablePluginIds.Count);
			Assert.AreEqual("platform", evaluation.AvailablePluginIds[0]);
			Assert.AreEqual(0, evaluation.MissingRequiredPluginIds.Count);
			Assert.IsTrue(evaluation.HasAllRequiredPluginsAvailable);
		}
		finally
		{
			if (Directory.Exists(pluginCatalogRootPath))
				Directory.Delete(pluginCatalogRootPath, true);
		}
	}

	[TestMethod]
	public void EvaluateRequiredPlugins_ShouldDiscoverPlatformCoreFromPluginCatalogDefinition()
	{
		string pluginCatalogRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string pluginRootPath = Path.Combine(pluginCatalogRootPath, "_managed", "manoir-plugincatalog", "plugins", "Core", "MainServices", "Platform");

		try
		{
			Directory.CreateDirectory(Path.Combine(pluginRootPath, "deploy"));
			File.WriteAllText(Path.Combine(pluginRootPath, "plugin.yaml"), @"
apiVersion: manoir/v1
kind: AvailablePlugin
repoUrl: https://github.com/manoir-app/MaNoir.Platform
displayName: MaNoir Platform Core
version: 0.0.0-dev
minimumMaNoirVersion: 0.0.0
deployment:
  group: platform
  adminUi:
    pathPrefix: /platform
    composeService: core
    port: 8080
  artifacts:
    - kind: compose
      path: deploy/docker-compose.yml
");
			File.WriteAllText(Path.Combine(pluginRootPath, "deploy", "docker-compose.yml"), "services:\n  core:\n    image: ghcr.io/manoir-app/manoir-core-adminui:latest\n");

			RequiredPluginAvailabilityEvaluation evaluation = LocalPluginCatalogInspector.EvaluateRequiredPlugins(pluginCatalogRootPath, ["platform"]);

			Assert.AreEqual(1, evaluation.AvailablePluginIds.Count);
			Assert.AreEqual("platform", evaluation.AvailablePluginIds[0]);
			Assert.AreEqual(0, evaluation.MissingRequiredPluginIds.Count);
			Assert.AreEqual(0, evaluation.Errors.Count);
		}
		finally
		{
			if (Directory.Exists(pluginCatalogRootPath))
				Directory.Delete(pluginCatalogRootPath, true);
		}
	}
}