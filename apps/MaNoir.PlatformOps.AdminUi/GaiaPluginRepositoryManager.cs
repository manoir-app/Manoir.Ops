using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaPluginRepositoryManager
{
	public const string PluginRepositoriesEnvironmentVariableName = "MANOIR_PLUGINS_REPO";

	public const string DefaultPluginRepositoryUrl = "https://github.com/manoir-app/Manoir.PluginCatalog";

	private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<GaiaCommandExecutionResult>> _runCommandAsync;

	public GaiaPluginRepositoryManager()
		: this(RunCommandAsync)
	{
	}

	public GaiaPluginRepositoryManager(Func<string, IReadOnlyList<string>, CancellationToken, Task<GaiaCommandExecutionResult>> runCommandAsync)
	{
		_runCommandAsync = runCommandAsync ?? throw new ArgumentNullException(nameof(runCommandAsync));
	}

	public GaiaPluginRepositoryConfiguration ResolveConfiguration(IReadOnlyList<string> persistedRepositoryUrls)
	{
		IReadOnlyList<string> persistedUrls = NormalizeRepositoryUrls(persistedRepositoryUrls);
		if (persistedUrls.Count > 0)
		{
			return new GaiaPluginRepositoryConfiguration()
			{
				RepositoryUrls = persistedUrls,
				Source = "persisted",
				UsesDefaultRepository = false
			};
		}

		IReadOnlyList<string> environmentUrls = NormalizeRepositoryUrls([Environment.GetEnvironmentVariable(PluginRepositoriesEnvironmentVariableName)]);
		if (environmentUrls.Count > 0)
		{
			return new GaiaPluginRepositoryConfiguration()
			{
				RepositoryUrls = environmentUrls,
				Source = "environment",
				UsesDefaultRepository = false
			};
		}

		return new GaiaPluginRepositoryConfiguration()
		{
			RepositoryUrls = [DefaultPluginRepositoryUrl],
			Source = "default",
			UsesDefaultRepository = true
		};
	}

	public bool IsPluginCatalogEmpty(string pluginRepositoriesRootPath)
	{
		if (string.IsNullOrWhiteSpace(pluginRepositoriesRootPath) || !Directory.Exists(pluginRepositoriesRootPath))
			return true;

		return !Directory.EnumerateFiles(pluginRepositoriesRootPath, PluginRepositoryDeploymentLoader.DefaultManifestFileName, SearchOption.AllDirectories).Any();
	}

	public async Task<GaiaPluginRepositorySyncResult> SyncAsync(
		string pluginRepositoriesRootPath,
		IReadOnlyList<string> repositoryUrls,
		IReadOnlyList<GaiaManagedPluginRepositoryState> currentManagedRepositories,
		CancellationToken cancellationToken = default)
	{
		string resolvedPluginRepositoriesRootPath = Path.GetFullPath(pluginRepositoriesRootPath ?? throw new ArgumentNullException(nameof(pluginRepositoriesRootPath)));
		string managedRepositoriesRootPath = ResolveManagedRepositoriesRootPath(resolvedPluginRepositoriesRootPath);
		Directory.CreateDirectory(resolvedPluginRepositoriesRootPath);
		Directory.CreateDirectory(managedRepositoriesRootPath);

		IReadOnlyList<string> normalizedRepositoryUrls = NormalizeRepositoryUrls(repositoryUrls);
		Dictionary<string, GaiaManagedPluginRepositoryState> existingRepositoriesByUrl = (currentManagedRepositories ?? Array.Empty<GaiaManagedPluginRepositoryState>())
			.Where(repository => repository != null && !string.IsNullOrWhiteSpace(repository.RepositoryUrl) && !string.IsNullOrWhiteSpace(repository.LocalDirectoryName))
			.GroupBy(repository => repository.RepositoryUrl.Trim(), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

		HashSet<string> reservedDirectoryNames = new HashSet<string>(
			(currentManagedRepositories ?? Array.Empty<GaiaManagedPluginRepositoryState>())
				.Where(repository => repository != null && !string.IsNullOrWhiteSpace(repository.LocalDirectoryName))
				.Select(repository => repository.LocalDirectoryName),
			StringComparer.OrdinalIgnoreCase);

		List<GaiaManagedPluginRepositoryState> synchronizedRepositories = new List<GaiaManagedPluginRepositoryState>();
		List<string> messages = new List<string>();
		List<string> errors = new List<string>();

		foreach (string repositoryUrl in normalizedRepositoryUrls)
		{
			GaiaManagedPluginRepositoryState existingRepository = existingRepositoriesByUrl.TryGetValue(repositoryUrl, out GaiaManagedPluginRepositoryState value)
				? value
				: null;
			string localDirectoryName = !string.IsNullOrWhiteSpace(existingRepository?.LocalDirectoryName)
				? existingRepository.LocalDirectoryName
				: CreateManagedDirectoryName(repositoryUrl, reservedDirectoryNames);
			string repositoryRootPath = Path.Combine(managedRepositoriesRootPath, localDirectoryName);

			reservedDirectoryNames.Add(localDirectoryName);
			synchronizedRepositories.Add(new GaiaManagedPluginRepositoryState()
			{
				RepositoryUrl = repositoryUrl,
				LocalDirectoryName = localDirectoryName
			});

			try
			{
				if (!Directory.Exists(repositoryRootPath))
				{
					messages.Add("Cloning plugin repository '" + repositoryUrl + "'.");
					GaiaCommandExecutionResult cloneResult = await _runCommandAsync(
						resolvedPluginRepositoriesRootPath,
						["clone", "--depth", "1", repositoryUrl, repositoryRootPath],
						cancellationToken);
					EnsureSuccess(cloneResult, repositoryUrl, "clone");
					messages.Add("Plugin repository '" + repositoryUrl + "' cloned.");
					continue;
				}

				if (!Directory.Exists(Path.Combine(repositoryRootPath, ".git")))
				{
					errors.Add("Managed plugin repository '" + repositoryUrl + "' exists at '" + repositoryRootPath + "' but is not a git repository.");
					continue;
				}

				messages.Add("Updating plugin repository '" + repositoryUrl + "'.");
				GaiaCommandExecutionResult pullResult = await _runCommandAsync(repositoryRootPath, ["pull", "--ff-only"], cancellationToken);
				EnsureSuccess(pullResult, repositoryUrl, "pull");
				messages.Add("Plugin repository '" + repositoryUrl + "' updated.");
			}
			catch (Exception exception)
			{
				errors.Add("Plugin repository '" + repositoryUrl + "' could not be synchronized: " + exception.Message);
			}
		}

		HashSet<string> targetRepositoryUrls = normalizedRepositoryUrls.ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (GaiaManagedPluginRepositoryState existingRepository in currentManagedRepositories ?? Array.Empty<GaiaManagedPluginRepositoryState>())
		{
			if (existingRepository == null || string.IsNullOrWhiteSpace(existingRepository.RepositoryUrl) || string.IsNullOrWhiteSpace(existingRepository.LocalDirectoryName))
				continue;

			if (targetRepositoryUrls.Contains(existingRepository.RepositoryUrl.Trim()))
				continue;

			string repositoryRootPath = Path.Combine(managedRepositoriesRootPath, existingRepository.LocalDirectoryName);
			if (!Directory.Exists(repositoryRootPath))
				continue;

			try
			{
				Directory.Delete(repositoryRootPath, recursive: true);
				messages.Add("Managed plugin repository '" + existingRepository.RepositoryUrl + "' removed.");
			}
			catch (Exception exception)
			{
				errors.Add("Managed plugin repository '" + existingRepository.RepositoryUrl + "' could not be removed: " + exception.Message);
			}
		}

		return new GaiaPluginRepositorySyncResult()
		{
			ManagedRepositories = synchronizedRepositories,
			Messages = messages,
			Errors = errors,
			SynchronizedAtUtc = DateTimeOffset.UtcNow
		};
	}

	public static IReadOnlyList<string> NormalizeRepositoryUrls(IEnumerable<string> values)
	{
		List<string> normalizedValues = new List<string>();
		HashSet<string> seenValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (string value in values ?? Array.Empty<string>())
		{
			if (string.IsNullOrWhiteSpace(value))
				continue;

			foreach (string part in value.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				string trimmedValue = part.Trim();
				if (trimmedValue.Length == 0 || !seenValues.Add(trimmedValue))
					continue;

				normalizedValues.Add(trimmedValue);
			}
		}

		return normalizedValues;
	}

	public static string ResolveManagedRepositoriesRootPath(string pluginRepositoriesRootPath)
	{
		return Path.Combine(Path.GetFullPath(pluginRepositoriesRootPath), "_managed");
	}

	private static string CreateManagedDirectoryName(string repositoryUrl, HashSet<string> reservedDirectoryNames)
	{
		string repositoryName = TryGetRepositoryName(repositoryUrl);
		string baseDirectoryName = PlatformOpsNaming.NormalizeSegment(repositoryName, "plugin-repository") ?? "plugin-repository";
		string directoryName = baseDirectoryName;
		int suffix = 2;

		while (reservedDirectoryNames.Contains(directoryName))
		{
			directoryName = baseDirectoryName + "-" + suffix;
			suffix++;
		}

		return directoryName;
	}

	private static string TryGetRepositoryName(string repositoryUrl)
	{
		if (Uri.TryCreate(repositoryUrl, UriKind.Absolute, out Uri uri))
		{
			string segment = uri.Segments.LastOrDefault();
			if (!string.IsNullOrWhiteSpace(segment))
				return segment.Trim('/').Replace(".git", string.Empty, StringComparison.OrdinalIgnoreCase);
		}

		string[] parts = repositoryUrl.Split(['/','\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return parts.Length == 0
			? repositoryUrl
			: parts[^1].Replace(".git", string.Empty, StringComparison.OrdinalIgnoreCase);
	}

	private static void EnsureSuccess(GaiaCommandExecutionResult result, string repositoryUrl, string operation)
	{
		if (result == null)
			throw new InvalidOperationException("The git command produced no result.");

		if (result.ExitCode == 0)
			return;

		string details = string.IsNullOrWhiteSpace(result.StandardError)
			? result.StandardOutput
			: result.StandardError;

		throw new InvalidOperationException("git " + operation + " failed for '" + repositoryUrl + "': " + details.Trim());
	}

	private static async Task<GaiaCommandExecutionResult> RunCommandAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		using Process process = new Process();
		process.StartInfo = new ProcessStartInfo()
		{
			FileName = "git",
			WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		foreach (string argument in arguments ?? Array.Empty<string>())
			process.StartInfo.ArgumentList.Add(argument);

		process.Start();
		Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);

		return new GaiaCommandExecutionResult()
		{
			ExitCode = process.ExitCode,
			StandardOutput = await standardOutputTask,
			StandardError = await standardErrorTask
		};
	}
}

public sealed class GaiaPluginRepositoryConfiguration
{
	public IReadOnlyList<string> RepositoryUrls { get; set; } = Array.Empty<string>();

	public string Source { get; set; }

	public bool UsesDefaultRepository { get; set; }
}

public sealed class GaiaPluginRepositorySyncResult
{
	public IReadOnlyList<GaiaManagedPluginRepositoryState> ManagedRepositories { get; set; } = Array.Empty<GaiaManagedPluginRepositoryState>();

	public IReadOnlyList<string> Messages { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

	public DateTimeOffset? SynchronizedAtUtc { get; set; }
}

public sealed class GaiaManagedPluginRepositoryState
{
	public string RepositoryUrl { get; set; }

	public string LocalDirectoryName { get; set; }
}

public sealed class GaiaCommandExecutionResult
{
	public int ExitCode { get; set; }

	public string StandardOutput { get; set; }

	public string StandardError { get; set; }
}

public sealed class GaiaPluginRepositoriesSnapshot
{
	public string PluginRepositoriesRootPath { get; set; }

	public string ManagedRepositoriesRootPath { get; set; }

	public string ConfigurationSource { get; set; }

	public bool UsesDefaultRepository { get; set; }

	public bool IsPluginCatalogEmpty { get; set; }

	public DateTimeOffset? LastSynchronizedUtc { get; set; }

	public IReadOnlyList<string> ConfiguredRepositoryUrls { get; set; } = Array.Empty<string>();

	public IReadOnlyList<GaiaPluginRepositoryEntry> Repositories { get; set; } = Array.Empty<GaiaPluginRepositoryEntry>();

	public IReadOnlyList<string> LastSyncMessages { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> LastSyncErrors { get; set; } = Array.Empty<string>();
}

public sealed class GaiaPluginRepositoryEntry
{
	public string RepositoryUrl { get; set; }

	public string LocalDirectoryName { get; set; }

	public string LocalPath { get; set; }

	public bool Exists { get; set; }

	public bool IsManaged { get; set; }
}