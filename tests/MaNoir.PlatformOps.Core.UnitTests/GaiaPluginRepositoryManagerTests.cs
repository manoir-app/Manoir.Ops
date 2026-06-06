using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.AdminUi;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class GaiaPluginRepositoryManagerTests
{
	[TestMethod]
	public void ResolveConfiguration_ShouldFallbackToDefaultRepository()
	{
		using EnvironmentVariableScope pluginsRepoScope = new EnvironmentVariableScope(GaiaPluginRepositoryManager.PluginRepositoriesEnvironmentVariableName, null);

		GaiaPluginRepositoryManager manager = new GaiaPluginRepositoryManager();
		GaiaPluginRepositoryConfiguration configuration = manager.ResolveConfiguration(Array.Empty<string>());

		Assert.AreEqual("default", configuration.Source);
		Assert.AreEqual(1, configuration.RepositoryUrls.Count);
		Assert.AreEqual(GaiaPluginRepositoryManager.DefaultPluginRepositoryUrl, configuration.RepositoryUrls[0]);
		Assert.IsTrue(configuration.UsesDefaultRepository);
	}

	[TestMethod]
	public void ResolveConfiguration_ShouldUseEnvironmentWhenPersistedConfigurationIsEmpty()
	{
		using EnvironmentVariableScope pluginsRepoScope = new EnvironmentVariableScope(
			GaiaPluginRepositoryManager.PluginRepositoriesEnvironmentVariableName,
			"https://github.com/manoir-app/Manoir.PluginCatalog,https://github.com/manoir-app/UnAutreRepo");

		GaiaPluginRepositoryManager manager = new GaiaPluginRepositoryManager();
		GaiaPluginRepositoryConfiguration configuration = manager.ResolveConfiguration(Array.Empty<string>());

		Assert.AreEqual("environment", configuration.Source);
		CollectionAssert.AreEqual(
			new[]
			{
				"https://github.com/manoir-app/Manoir.PluginCatalog",
				"https://github.com/manoir-app/UnAutreRepo"
			},
			configuration.RepositoryUrls.ToArray());
		Assert.IsFalse(configuration.UsesDefaultRepository);
	}

	[TestMethod]
	public async Task SyncAsync_ShouldCloneConfiguredRepositoriesAndRemoveStaleManagedDirectory()
	{
		string pluginRepositoriesRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string managedRepositoriesRootPath = GaiaPluginRepositoryManager.ResolveManagedRepositoriesRootPath(pluginRepositoriesRootPath);
		List<string> commands = new List<string>();

		try
		{
			Directory.CreateDirectory(managedRepositoriesRootPath);
			Directory.CreateDirectory(Path.Combine(managedRepositoriesRootPath, "stale-repository", ".git"));

			GaiaPluginRepositoryManager manager = new GaiaPluginRepositoryManager((workingDirectory, arguments, cancellationToken) =>
			{
				commands.Add((workingDirectory ?? string.Empty) + " => " + string.Join(" ", arguments));

				if (arguments.Count > 0 && string.Equals(arguments[0], "clone", StringComparison.OrdinalIgnoreCase))
				{
					Directory.CreateDirectory(arguments[4]);
					Directory.CreateDirectory(Path.Combine(arguments[4], ".git"));
				}

				return Task.FromResult(new GaiaCommandExecutionResult()
				{
					ExitCode = 0,
					StandardOutput = "ok",
					StandardError = string.Empty
				});
			});

			GaiaPluginRepositorySyncResult result = await manager.SyncAsync(
				pluginRepositoriesRootPath,
				["https://github.com/manoir-app/Manoir.PluginCatalog"],
				[
					new GaiaManagedPluginRepositoryState()
					{
						RepositoryUrl = "https://github.com/manoir-app/AncienRepo",
						LocalDirectoryName = "stale-repository"
					}
				],
				CancellationToken.None);

			Assert.AreEqual(1, result.ManagedRepositories.Count);
			Assert.AreEqual("https://github.com/manoir-app/Manoir.PluginCatalog", result.ManagedRepositories[0].RepositoryUrl);
			Assert.IsTrue(Directory.Exists(Path.Combine(managedRepositoriesRootPath, result.ManagedRepositories[0].LocalDirectoryName, ".git")));
			Assert.IsFalse(Directory.Exists(Path.Combine(managedRepositoriesRootPath, "stale-repository")));
			Assert.AreEqual(1, commands.Count(command => command.Contains(" clone ", StringComparison.Ordinal)));
			Assert.AreEqual(0, result.Errors.Count);
		}
		finally
		{
			if (Directory.Exists(pluginRepositoriesRootPath))
				Directory.Delete(pluginRepositoriesRootPath, true);
		}
	}

	[TestMethod]
	public async Task SyncAsync_ShouldMarkExistingRepositoryAsSafeBeforePull()
	{
		string pluginRepositoriesRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string managedRepositoriesRootPath = GaiaPluginRepositoryManager.ResolveManagedRepositoriesRootPath(pluginRepositoriesRootPath);
		string localDirectoryName = "manoir-plugincatalog";
		string repositoryRootPath = Path.Combine(managedRepositoriesRootPath, localDirectoryName);
		List<string> commands = new List<string>();

		try
		{
			Directory.CreateDirectory(Path.Combine(repositoryRootPath, ".git"));

			GaiaPluginRepositoryManager manager = new GaiaPluginRepositoryManager((workingDirectory, arguments, cancellationToken) =>
			{
				commands.Add((workingDirectory ?? string.Empty) + " => " + string.Join(" ", arguments));
				return Task.FromResult(new GaiaCommandExecutionResult()
				{
					ExitCode = 0,
					StandardOutput = "ok",
					StandardError = string.Empty
				});
			});

			GaiaPluginRepositorySyncResult result = await manager.SyncAsync(
				pluginRepositoriesRootPath,
				["https://github.com/manoir-app/Manoir.PluginCatalog"],
				[
					new GaiaManagedPluginRepositoryState()
					{
						RepositoryUrl = "https://github.com/manoir-app/Manoir.PluginCatalog",
						LocalDirectoryName = localDirectoryName
					}
				],
				CancellationToken.None);

			Assert.AreEqual(1, commands.Count);
			StringAssert.Contains(commands[0], repositoryRootPath + " => -c safe.directory=" + repositoryRootPath + " pull --ff-only");
			Assert.AreEqual(0, result.Errors.Count);
		}
		finally
		{
			if (Directory.Exists(pluginRepositoriesRootPath))
				Directory.Delete(pluginRepositoriesRootPath, true);
		}
	}
}