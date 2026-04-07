using System.Text.Json;
using System.Threading.Channels;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Options for configuring distributed notification fan-out.
/// </summary>
public class DistributedNotificationOptions
{
    /// <summary>
    /// Unique identifier for this host instance. Messages received from the bus
    /// with a matching host ID are skipped to prevent double-processing.
    /// Defaults to a new GUID if not set.
    /// </summary>
    public string HostId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The topic name used for publishing and subscribing to distributed notifications.
    /// Defaults to <c>"distributed-notifications"</c>.
    /// </summary>
    public string Topic { get; set; } = "distributed-notifications";

    /// <summary>
    /// Custom JSON serializer options for notification serialization/deserialization.
    /// When null, <see cref="JsonSerializerOptions.Default"/> is used.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Maximum capacity of the outbound subscription buffer.
    /// When full, the behavior is controlled by <see cref="FullMode"/>.
    /// Default is 1000.
    /// </summary>
    public int MaxCapacity { get; set; } = 1000;

    /// <summary>
    /// Behavior when the outbound subscription buffer is full.
    /// Default is <see cref="BoundedChannelFullMode.Wait"/> to provide backpressure
    /// and avoid dropping notifications.
    /// </summary>
    public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.Wait;

    /// <summary>
    /// Optional prefix applied to topic and per-node queue names for app-level scoping.
    /// When set, topic names become <c>"{ResourcePrefix}-{Topic}"</c>.
    /// When <c>null</c> or empty (default), names are used as-is.
    /// </summary>
    /// <remarks>
    /// Use this to isolate multiple applications sharing the same infrastructure
    /// (e.g., <c>"myapp"</c> produces topic <c>"myapp-distributed-notifications"</c>).
    /// </remarks>
    public string? ResourcePrefix { get; set; }

    /// <summary>
    /// Returns <see cref="Topic"/> with <see cref="ResourcePrefix"/> applied when configured.
    /// </summary>
    internal string EffectiveTopic =>
        string.IsNullOrEmpty(ResourcePrefix) ? Topic : $"{ResourcePrefix}-{Topic}";
}
