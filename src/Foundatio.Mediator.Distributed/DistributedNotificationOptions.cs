using System.Reflection;
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
    /// When <c>true</c>, all notification types are distributed via pub/sub,
    /// regardless of whether they implement <see cref="IDistributedNotification"/>
    /// or are decorated with <see cref="DistributedNotificationAttribute"/>.
    /// Default is <c>false</c>.
    /// </summary>
    public bool IncludeAllNotifications { get; set; }

    /// <summary>
    /// Optional predicate evaluated for notification types that are not already included
    /// by <see cref="IDistributedNotification"/>, <see cref="DistributedNotificationAttribute"/>,
    /// or explicit <see cref="Include{T}"/> calls. Return <c>true</c> to distribute the type.
    /// </summary>
    /// <example>
    /// <code>
    /// opts.MessageFilter = type => type.Namespace?.StartsWith("MyApp.Events") == true;
    /// </code>
    /// </example>
    public Func<Type, bool>? MessageFilter { get; set; }

    /// <summary>
    /// Explicitly includes a notification type for distributed fan-out.
    /// Use this when the type cannot implement <see cref="IDistributedNotification"/>
    /// or be decorated with <see cref="DistributedNotificationAttribute"/>.
    /// </summary>
    /// <typeparam name="T">The notification type to distribute.</typeparam>
    /// <returns>This options instance for chaining.</returns>
    public DistributedNotificationOptions Include<T>()
    {
        IncludedTypes.Add(typeof(T));
        return this;
    }

    /// <summary>
    /// Explicitly includes a notification type for distributed fan-out.
    /// </summary>
    /// <param name="type">The notification type to distribute.</param>
    /// <returns>This options instance for chaining.</returns>
    public DistributedNotificationOptions Include(Type type)
    {
        IncludedTypes.Add(type);
        return this;
    }

    /// <summary>
    /// Scans the assembly containing <typeparamref name="T"/> and includes all
    /// public notification types (classes and structs) for distributed fan-out.
    /// A type is considered a notification if it implements <see cref="INotification"/>.
    /// </summary>
    /// <typeparam name="T">A type whose assembly will be scanned.</typeparam>
    /// <returns>This options instance for chaining.</returns>
    public DistributedNotificationOptions IncludeNotificationsFromAssemblyOf<T>()
    {
        foreach (var type in typeof(T).Assembly.GetExportedTypes())
        {
            if (typeof(INotification).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
                IncludedTypes.Add(type);
        }

        return this;
    }

    /// <summary>
    /// Types explicitly added via <see cref="Include{T}"/> or <see cref="IncludeNotificationsFromAssemblyOf{T}"/>.
    /// </summary>
    internal HashSet<Type> IncludedTypes { get; } = [];

    /// <summary>
    /// Determines whether a given message type should be distributed via pub/sub.
    /// Evaluation order: explicit includes → <see cref="IDistributedNotification"/> →
    /// <see cref="DistributedNotificationAttribute"/> → <see cref="MessageFilter"/> →
    /// <see cref="IncludeAllNotifications"/>.
    /// </summary>
    public bool ShouldDistribute(Type messageType)
    {
        if (IncludedTypes.Contains(messageType))
            return true;

        if (typeof(IDistributedNotification).IsAssignableFrom(messageType))
            return true;

        if (messageType.GetCustomAttribute<DistributedNotificationAttribute>() is not null)
            return true;

        if (MessageFilter is not null)
            return MessageFilter(messageType);

        return IncludeAllNotifications;
    }

    /// <summary>
    /// Returns <see cref="Topic"/> with <see cref="ResourcePrefix"/> applied when configured.
    /// </summary>
    internal string EffectiveTopic =>
        string.IsNullOrEmpty(ResourcePrefix) ? Topic : $"{ResourcePrefix}-{Topic}";
}
