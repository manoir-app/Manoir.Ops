using Home.Common.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaPluginInstallationMessage : BaseMessage
{
    public const string TopicName = "system.extensions.install";

    public GaiaPluginInstallationMessage() : base(TopicName)
    {
    }

    public string RepositoryUrl { get; set; }

    public string OperationId { get; set; }
}

public sealed class GaiaPluginInstallationResponse : MessageResponse
{
    public string OperationId { get; set; }

    public string RepositoryUrl { get; set; }

    public string Status { get; set; }

    public string Message { get; set; }
}

public sealed class PluginRuntimeStateMessage : BaseMessage
{
    public const string PublishTopic = "system.plugin.runtime.state";

    public PluginRuntimeStateMessage() : base(PublishTopic)
    {
        Components = [];
    }

    public string PluginId { get; set; }

    public List<DeployedComponent> Components { get; set; }
}

[JsonConverter(typeof(StringEnumConverter))]
public enum DeployedComponentStatus
{
    Unknown,
    Running,
    Healthy,
    Unhealthy,
    Stopped,
    Failed
}

public sealed class DeployedComponent
{
    public string Type { get; set; }

    public string Name { get; set; }

    public DeployedComponentStatus Status { get; set; }

    public DateTimeOffset ObservedAtUtc { get; set; }
}