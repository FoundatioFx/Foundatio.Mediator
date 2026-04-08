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

    /// <summary>
    /// Controls whether queue worker hosted services are started. When <c>false</c>,
    /// queue middleware is still registered (so messages can be enqueued to a transport)
    /// but no <see cref="QueueWorker"/> instances are started in this process.
    /// Default is <c>true</c>.
    /// </summary>
    /// <remarks>
    /// Use this to run API-only nodes that enqueue work without processing it locally.
    /// Worker nodes in a separate process can then dequeue and handle those messages.
    /// </remarks>
    public bool WorkersEnabled { get; set; } = true;

    /// <summary>
    /// When set, only start workers whose queue name or <see cref="QueueAttribute.Group"/>
    /// matches an entry in this set. Both queue names (e.g., <c>"DemoExportJob"</c>) and
    /// group names (e.g., <c>"exports"</c>) are accepted.
    /// When <c>null</c> or empty, all queue workers are started (subject to
    /// <see cref="Group"/> and <see cref="WorkersEnabled"/> filtering).
    /// </summary>
    /// <remarks>
    /// This is more granular than <see cref="Group"/>, which filters by a single group name.
    /// <see cref="Queues"/> accepts multiple values and matches against both queue names and group names.
    /// When both <see cref="Group"/> and <see cref="Queues"/> are set, both filters are applied.
    /// </remarks>
    public HashSet<string>? Queues { get; set; }

    /// <summary>
    /// Optional prefix applied to all queue names for app-level scoping.
    /// When set, queue names become <c>"{ResourcePrefix}-{QueueName}"</c>.
    /// When <c>null</c> or empty (default), queue names are used as-is.
    /// </summary>
    /// <remarks>
    /// Use this to isolate multiple applications sharing the same infrastructure
    /// (e.g., <c>"myapp"</c> produces queues like <c>"myapp-CreateOrder"</c>).
    /// Dead-letter queues inherit the prefix automatically.
    /// </remarks>
    public string? ResourcePrefix { get; set; }

    /// <summary>
    /// Applies <see cref="ResourcePrefix"/> to the given queue name.
    /// Returns the name unchanged when no prefix is configured.
    /// </summary>
    public string ApplyPrefix(string name) =>
        string.IsNullOrEmpty(ResourcePrefix) ? name : $"{ResourcePrefix}-{name}";
}
