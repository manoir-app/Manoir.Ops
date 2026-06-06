using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaRuntimeStateStore
{
	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly string _stateFilePath;

	public GaiaRuntimeStateStore(string stateFilePath)
	{
		if (string.IsNullOrWhiteSpace(stateFilePath))
			throw new ArgumentException("A state file path is required.", nameof(stateFilePath));

		_stateFilePath = Path.GetFullPath(stateFilePath);
	}

	public GaiaPersistedRuntimeState Load()
	{
		if (!File.Exists(_stateFilePath))
			return null;

		string json = File.ReadAllText(_stateFilePath);
		if (string.IsNullOrWhiteSpace(json))
			return null;

		return JsonSerializer.Deserialize<GaiaPersistedRuntimeState>(json, SerializerOptions);
	}

	public void Save(GaiaPersistedRuntimeState state)
	{
		ArgumentNullException.ThrowIfNull(state);

		string directoryPath = Path.GetDirectoryName(_stateFilePath);
		if (!string.IsNullOrWhiteSpace(directoryPath))
			Directory.CreateDirectory(directoryPath);

		string json = JsonSerializer.Serialize(state, SerializerOptions);
		File.WriteAllText(_stateFilePath, json);
	}

	public string StateFilePath => _stateFilePath;
}

public sealed class GaiaPersistedRuntimeState
{
	public DateTimeOffset? LastInspectionUtc { get; set; }

	public DateTimeOffset? LastEnsureUtc { get; set; }

	public DateTimeOffset? LastPluginRepositorySyncUtc { get; set; }

	public AdminUiDeploymentProjection[] AdminUiDeployments { get; set; } = Array.Empty<AdminUiDeploymentProjection>();

	public AdminUiDeploymentDiff[] AdminUiDeploymentDiffs { get; set; } = Array.Empty<AdminUiDeploymentDiff>();

	public string[] ConfiguredPluginRepositoryUrls { get; set; } = Array.Empty<string>();

	public GaiaManagedPluginRepositoryState[] ManagedPluginRepositories { get; set; } = Array.Empty<GaiaManagedPluginRepositoryState>();

	public string[] LastPluginRepositorySyncMessages { get; set; } = Array.Empty<string>();

	public string[] LastPluginRepositorySyncErrors { get; set; } = Array.Empty<string>();
}