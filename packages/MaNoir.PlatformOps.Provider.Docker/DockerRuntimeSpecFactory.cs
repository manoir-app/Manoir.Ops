using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public static class DockerRuntimeSpecFactory
{
	public const string SharedNetworkName = "manoir";

	public static DockerRuntimeSpec Create(DockerDeploymentPlan plan, IReadOnlyCollection<int> usedHostPorts)
	{
		if (plan == null)
			throw new ArgumentNullException(nameof(plan));

		HashSet<int> reservedHostPorts = new HashSet<int>(usedHostPorts ?? Array.Empty<int>());
		List<string> errors = new List<string>();
		List<DockerRuntimeServiceSpec> services = new List<DockerRuntimeServiceSpec>();
		string composeDirectoryPath = string.IsNullOrWhiteSpace(plan.ComposeFilePath)
			? plan.RepositoryRootPath
			: Path.GetDirectoryName(plan.ComposeFilePath);

		foreach (DockerDeploymentServicePlan service in plan.Services)
		{
			if (service == null)
				continue;

			if (!string.IsNullOrWhiteSpace(service.BuildContext))
			{
				errors.Add($"services.{service.Name}.build is not supported by the Docker runtime executor.");
				continue;
			}

			if (string.IsNullOrWhiteSpace(service.Image))
			{
				errors.Add($"services.{service.Name}.image is required by the Docker runtime executor.");
				continue;
			}

			List<DockerRuntimePortBinding> portBindings = ParsePortBindings(service, reservedHostPorts, errors);
			List<DockerRuntimeMount> mounts = ParseMounts(service, composeDirectoryPath, errors);
			AppendAutomaticPlatformMounts(plan.PluginId, plan.RepositoryRootPath, mounts);
			Dictionary<string, string> environmentByName = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (DockerResolvedEnvironmentEntry automaticEntry in DockerAutomaticEnvironmentVariables.CreateResolvedEntries(plan.PluginId))
				environmentByName[automaticEntry.Name] = automaticEntry.Value ?? string.Empty;

			foreach (DockerResolvedEnvironmentEntry entry in service.ResolvedEnvironment)
			{
				if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
					continue;

				environmentByName[entry.Name] = entry.Value ?? string.Empty;
			}

			List<string> environment = environmentByName
				.Select(pair => pair.Key + "=" + pair.Value)
				.ToList();

			services.Add(new DockerRuntimeServiceSpec()
			{
				ServiceName = service.Name,
				ContainerName = ResolveContainerName(plan.PluginId, service),
				NetworkAliases = ResolveNetworkAliases(plan.PluginId, service),
				Image = service.Image,
				RestartPolicy = service.RestartPolicy,
				Command = service.Command,
				Environment = environment,
				PortBindings = portBindings,
				Mounts = mounts
			});
		}

		if (errors.Count > 0)
			throw new DockerRuntimePlanningException(errors);

		return new DockerRuntimeSpec()
		{
			PluginId = plan.PluginId,
			NetworkName = SharedNetworkName,
			Services = services
		};
	}

	public static string ResolveContainerName(string pluginId, DockerDeploymentServicePlan service)
	{
		if (service == null)
			throw new ArgumentNullException(nameof(service));

		if (!string.IsNullOrWhiteSpace(service.ContainerName))
			return service.ContainerName;

		string normalizedPluginId = PlatformOpsNaming.RequireSegment(pluginId, "plugin", nameof(pluginId));
		string normalizedServiceName = PlatformOpsNaming.RequireSegment(service.Name, "service", nameof(service.Name));
		return normalizedPluginId + "-" + normalizedServiceName;
	}

	public static IReadOnlyList<string> ResolveNetworkAliases(string pluginId, DockerDeploymentServicePlan service)
	{
		string containerName = ResolveContainerName(pluginId, service);
		string serviceAlias = PlatformOpsNaming.RequireSegment(service.Name, "service", nameof(service.Name));

		if (string.Equals(containerName, serviceAlias, StringComparison.Ordinal))
			return [containerName];

		return [containerName, serviceAlias];
	}

	private static List<DockerRuntimePortBinding> ParsePortBindings(DockerDeploymentServicePlan service, HashSet<int> reservedHostPorts, List<string> errors)
	{
		List<DockerRuntimePortBinding> portBindings = new List<DockerRuntimePortBinding>();

		for (int index = 0; index < service.Ports.Count; index++)
		{
			string portValue = service.Ports[index];
			if (string.IsNullOrWhiteSpace(portValue))
				continue;

			string trimmedPortValue = portValue.Trim();
			string[] protocolParts = trimmedPortValue.Split('/');
			if (protocolParts.Length > 2)
			{
				errors.Add($"services.{service.Name}.ports[{index}] is invalid.");
				continue;
			}

			string protocol = protocolParts.Length == 2 ? protocolParts[1].Trim().ToLowerInvariant() : "tcp";
			string bindingValue = protocolParts[0].Trim();

			string[] bindingParts = bindingValue.Split(':');
			if (bindingParts.Length > 2)
			{
				errors.Add($"services.{service.Name}.ports[{index}] only supports 'containerPort' or 'hostPort:containerPort'.");
				continue;
			}

			if (!TryParsePort(bindingParts[^1], out int containerPort))
			{
				errors.Add($"services.{service.Name}.ports[{index}] contains an invalid container port.");
				continue;
			}

			int hostPort;
			if (bindingParts.Length == 2)
			{
				if (!TryParsePort(bindingParts[0], out hostPort))
				{
					errors.Add($"services.{service.Name}.ports[{index}] contains an invalid host port.");
					continue;
				}

				if (!reservedHostPorts.Add(hostPort))
				{
					errors.Add($"services.{service.Name}.ports[{index}] requires host port '{hostPort}', but it is already reserved.");
					continue;
				}
			}
			else
			{
				hostPort = containerPort;
				while (!reservedHostPorts.Add(hostPort))
					hostPort++;
			}

			portBindings.Add(new DockerRuntimePortBinding()
			{
				Protocol = protocol,
				ContainerPort = containerPort,
				HostPort = hostPort
			});
		}

		return portBindings;
	}

	private static List<DockerRuntimeMount> ParseMounts(DockerDeploymentServicePlan service, string composeDirectoryPath, List<string> errors)
	{
		List<DockerRuntimeMount> mounts = new List<DockerRuntimeMount>();

		for (int index = 0; index < service.Volumes.Count; index++)
		{
			string volumeValue = service.Volumes[index];
			if (string.IsNullOrWhiteSpace(volumeValue))
				continue;

			if (!TryParseMount(volumeValue.Trim(), composeDirectoryPath, out DockerRuntimeMount mount))
			{
				errors.Add($"services.{service.Name}.volumes[{index}] is invalid.");
				continue;
			}

			mounts.Add(mount);
		}

		return mounts;
	}

	private static void AppendAutomaticPlatformMounts(string pluginId, string sharedServicesRootPath, List<DockerRuntimeMount> mounts)
	{
		if (string.Equals(pluginId, DockerSharedServicesCatalog.SharedServicesPluginId, StringComparison.OrdinalIgnoreCase))
			return;

		if (mounts.Any(mount => mount.Kind == DockerRuntimeMountKind.Bind && string.Equals(mount.Target, DockerSharedServicesCatalog.HomeAutomationRootContainerPath, StringComparison.Ordinal)))
			return;

		mounts.Add(new DockerRuntimeMount()
		{
			Kind = DockerRuntimeMountKind.Bind,
			Source = DockerSharedServicesCatalog.ResolveDockerHostHomeAutomationRootPath(sharedServicesRootPath),
			Target = DockerSharedServicesCatalog.HomeAutomationRootContainerPath,
			IsReadOnly = false
		});
	}

	private static bool TryParseMount(string value, string composeDirectoryPath, out DockerRuntimeMount mount)
	{
		mount = null;
		string[] parts = SplitMountParts(value);
		if (parts.Length < 2 || parts.Length > 3)
			return false;

		string source = parts[0].Trim();
		string target = parts[1].Trim();
		string mode = parts.Length == 3 ? parts[2].Trim() : null;

		if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
			return false;

		bool isReadOnly = string.Equals(mode, "ro", StringComparison.OrdinalIgnoreCase);
		if (!string.IsNullOrWhiteSpace(mode) && !string.Equals(mode, "ro", StringComparison.OrdinalIgnoreCase) && !string.Equals(mode, "rw", StringComparison.OrdinalIgnoreCase))
			return false;

		DockerRuntimeMountKind kind = IsBindMountSource(source) ? DockerRuntimeMountKind.Bind : DockerRuntimeMountKind.Volume;
		if (kind == DockerRuntimeMountKind.Bind)
		{
			source = ResolveBindMountSource(source, composeDirectoryPath);
		}

		mount = new DockerRuntimeMount()
		{
			Kind = kind,
			Source = source,
			Target = target,
			IsReadOnly = isReadOnly
		};
		return true;
	}

	private static string[] SplitMountParts(string value)
	{
		if (value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/'))
		{
			string drivePrefix = value.Substring(0, 2);
			string[] remainder = value.Substring(2).Split(':');
			if (remainder.Length > 0)
				remainder[0] = drivePrefix + remainder[0];
			return remainder;
		}

		return value.Split(':');
	}

	private static bool IsBindMountSource(string source)
	{
		if (IsAbsoluteBindMountSource(source))
			return true;

		return source.StartsWith("./", StringComparison.Ordinal)
			|| source.StartsWith("../", StringComparison.Ordinal)
			|| source.StartsWith(".\\", StringComparison.Ordinal)
			|| source.StartsWith("..\\", StringComparison.Ordinal)
			|| source.Contains("/", StringComparison.Ordinal)
			|| source.Contains("\\", StringComparison.Ordinal);
	}

	private static bool IsAbsoluteBindMountSource(string source)
	{
		if (Path.IsPathRooted(source))
			return true;

		return IsWindowsAbsoluteBindMountSource(source);
	}

	private static string ResolveBindMountSource(string source, string composeDirectoryPath)
	{
		if (Path.IsPathRooted(source))
			return Path.GetFullPath(source);

		if (IsWindowsAbsoluteBindMountSource(source))
			return source;

		return Path.GetFullPath(Path.Combine(composeDirectoryPath ?? string.Empty, source));
	}

	private static bool IsWindowsAbsoluteBindMountSource(string source)
	{
		return source.Length >= 3
			&& char.IsLetter(source[0])
			&& source[1] == ':'
			&& (source[2] == '\\' || source[2] == '/');
	}

	private static bool TryParsePort(string value, out int port)
	{
		if (!int.TryParse(value.Trim(), out port))
			return false;

		return port is > 0 and <= 65535;
	}
}