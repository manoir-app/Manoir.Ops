using System;
using System.Collections.Generic;
using System.Reflection;
using Home.Common.Messages;
using MaNoir.Core.Contracts.Models.Agents;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaAgentRuntime
{
	private static readonly string[] FixedMessageTopics =
	[
		"gaia.>",
		"system.extensions.>",
		"security.>",
		"monitoring.>"
	];

	private static readonly List<string> FixedCapabilities =
	[
		"platformops",
		"runtime.bootstrap",
		"runtime.deployments",
		"runtime.reverse-proxy",
		"runtime.messaging"
	];

	public string AgentId => "gaia";

	public string MeshId => ResolveEnvironmentValue("MANOIR_MESH_ID", "local");

	public string DisplayName => "Gaia";

	public string Version => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

	public TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(30);

	public string[] MessageTopics => [.. FixedMessageTopics];

	public List<string> Capabilities => [.. FixedCapabilities];

	public void ReportStarting()
	{
		LogInfo($"Starting agent {AgentId} for mesh {MeshId}.");
	}

	public void ReportWaitingForMinimumVital()
	{
		LogInfo($"Agent {AgentId} is waiting for the minimum vital bootstrap before registration.");
	}

	public void ReportHeartbeat()
	{
		LogInfo($"Heartbeat emitted by agent {AgentId} for mesh {MeshId}.");
	}

	public void ReportRegistrationSucceeded(RegisteredAgent agent)
	{
		LogInfo($"Agent {agent.AgentId} registered in mesh {agent.MeshId} with state {agent.State}.");
	}

	public void ReportRegistrationFailed(Exception exception)
	{
		LogWarning($"Agent {AgentId} could not register in mesh {MeshId}.", exception);
	}

	public void ReportHeartbeatFailed(Exception exception)
	{
		LogWarning($"Heartbeat failed for agent {AgentId} in mesh {MeshId}.", exception);
	}

	public void ReportStopping()
	{
		LogInfo($"Stopping agent {AgentId}.");
	}

	public void ReportTopicsSubscribed()
	{
		LogInfo($"Agent {AgentId} subscribed to topics: {string.Join(", ", MessageTopics)}.");
	}

	public void ReportEnvironmentConfigured(IReadOnlyDictionary<string, string> variables)
	{
		if (variables == null || variables.Count == 0)
		{
			LogInfo($"Agent {AgentId} runtime environment was already configured.");
			return;
		}

		LogInfo($"Agent {AgentId} configured runtime environment variables: {string.Join(", ", variables.Keys)}.");
	}

	public void ReportInterprocessStopped()
	{
		LogInfo($"Interprocess listener stopped for agent {AgentId}.");
	}

	public void ReportMessageTriggeredOperation(string topic, string operation)
	{
		LogInfo($"Received topic '{topic}' and scheduled Gaia operation '{operation}'.");
	}

	public AgentRegistrationRequest CreateRegistrationRequest(AgentState state, string statusMessage = null)
	{
		return new AgentRegistrationRequest()
		{
			AgentId = AgentId,
			DisplayName = DisplayName,
			MeshId = MeshId,
			Version = Version,
			Capabilities = Capabilities,
			State = state,
			StatusMessage = statusMessage
		};
	}

	public AgentHeartbeatRequest CreateHeartbeatRequest(AgentState state, string statusMessage = null)
	{
		return new AgentHeartbeatRequest()
		{
			AgentId = AgentId,
			MeshId = MeshId,
			State = state,
			StatusMessage = statusMessage
		};
	}

	private static string ResolveEnvironmentValue(string environmentVariableName, string defaultValue)
	{
		string configuredValue = Environment.GetEnvironmentVariable(environmentVariableName);
		return string.IsNullOrWhiteSpace(configuredValue) ? defaultValue : configuredValue.Trim();
	}

	private static void LogInfo(string message)
	{
		Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] [Gaia] {message}");
	}

	private static void LogWarning(string message, Exception exception)
	{
		Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] [Gaia] WARNING {message} {exception.Message}");
	}
}