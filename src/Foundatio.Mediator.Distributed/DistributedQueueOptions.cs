using System.Text.Json;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Options for configuring distributed queue support.
/// </summary>
public class DistributedQueueOptions
{
    /// <summary>
    /// Custom JSON serializer options for message serialization/deserialization.
    /// When null, <see cref="JsonSerializerOptions.Default"/> is used.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// When set, only workers for queues in the matching group will be started.
    /// When null (default), all queue workers are started.
    /// </summary>
    public string? Group { get; set; }
}
