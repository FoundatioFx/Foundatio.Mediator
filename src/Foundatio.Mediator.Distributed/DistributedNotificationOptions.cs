using System.Collections.Concurrent;
using System.Reflection;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Options for bridging notifications across processes through an <see cref="Foundatio.Messaging.IMessageBus"/>.
/// </summary>
public class DistributedNotificationOptions
{
    /// <summary>
    /// Identifies this process so its own publications are not re-delivered to it. Defaults to a new id per process.
    /// </summary>
    public string HostId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Topic every distributed notification is published to.
    /// </summary>
    public string Topic { get; set; } = "distributed-notifications";

    /// <summary>
    /// Receives remote notifications on this host. Default is true. Set to false for a publisher-only
    /// host to skip its inbound subscription while continuing to publish distributed notifications.
    /// </summary>
    public bool ReceiveNotifications { get; set; } = true;

    /// <summary>
    /// Capacity of the outbound buffer between local publishes and the bus. A full buffer
    /// evicts the oldest notification and records a drop; publishing never waits for remote delivery.
    /// </summary>
    public int MaxCapacity { get; set; } = 1000;

    /// <summary>
    /// Maximum transport publications in flight. Default is 40, allowing transports to coalesce concurrent full batches.
    /// Set to 1 for sequential publication. The outbound buffer remains bounded by <see cref="MaxCapacity"/>.
    /// </summary>
    public int MaxConcurrentPublishes { get; set; } = 40;

    /// <summary>
    /// Prefix applied to the topic name.
    /// </summary>
    public string? ResourcePrefix { get; set; }

    /// <summary>
    /// Distributes every <see cref="INotification"/> unless excluded. Off by default.
    /// </summary>
    public bool IncludeAllNotifications { get; set; }

    /// <summary>
    /// Custom predicate consulted after the explicit include and exclude rules.
    /// </summary>
    public Func<Type, bool>? MessageFilter { get; set; }

    internal HashSet<Type> IncludedTypes { get; } = [];
    internal HashSet<Type> ExcludedTypes { get; } = [];
    internal List<Type> IncludedAssignableTo { get; } = [];

    private readonly ConcurrentDictionary<Type, bool> _shouldDistributeCache = new();

    /// <summary>
    /// Distributes notifications of exactly <typeparamref name="T"/>.
    /// </summary>
    public DistributedNotificationOptions Include<T>() => Include(typeof(T));

    /// <summary>
    /// Distributes notifications of exactly <paramref name="type"/>.
    /// </summary>
    public DistributedNotificationOptions Include(Type type)
    {
        IncludedTypes.Add(type);
        _shouldDistributeCache.Clear();
        return this;
    }

    /// <summary>
    /// Distributes every notification type assignable to <typeparamref name="T"/>, such as an event
    /// interface implemented by several concrete events.
    /// </summary>
    public DistributedNotificationOptions IncludeAssignableTo<T>() => IncludeAssignableTo(typeof(T));

    /// <summary>
    /// Distributes every notification type assignable to <paramref name="type"/>.
    /// </summary>
    public DistributedNotificationOptions IncludeAssignableTo(Type type)
    {
        IncludedAssignableTo.Add(type);
        _shouldDistributeCache.Clear();
        return this;
    }

    /// <summary>
    /// Never distributes <typeparamref name="T"/>, even when another rule would include it.
    /// </summary>
    public DistributedNotificationOptions Exclude<T>() => Exclude(typeof(T));

    /// <summary>
    /// Never distributes <paramref name="type"/>, even when another rule would include it.
    /// </summary>
    public DistributedNotificationOptions Exclude(Type type)
    {
        ExcludedTypes.Add(type);
        _shouldDistributeCache.Clear();
        return this;
    }

    /// <summary>
    /// Distributes every concrete <see cref="INotification"/> in the assembly containing <typeparamref name="T"/>.
    /// </summary>
    public DistributedNotificationOptions IncludeNotificationsFromAssemblyOf<T>()
    {
        foreach (var type in typeof(T).Assembly.GetExportedTypes())
        {
            if (typeof(INotification).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
                IncludedTypes.Add(type);
        }

        _shouldDistributeCache.Clear();
        return this;
    }

    /// <summary>
    /// Decides whether notifications of <paramref name="messageType"/> cross the bus. Exclusions win;
    /// then explicit includes, <see cref="IDistributedNotification"/>, <see cref="DistributedNotificationAttribute"/>,
    /// assignable-to includes, <see cref="MessageFilter"/>, and finally <see cref="IncludeAllNotifications"/>.
    /// </summary>
    public bool ShouldDistribute(Type messageType)
    {
        return _shouldDistributeCache.GetOrAdd(messageType, static (type, self) =>
        {
            if (self.ExcludedTypes.Contains(type))
                return false;

            if (self.IncludedTypes.Contains(type))
                return true;

            if (typeof(IDistributedNotification).IsAssignableFrom(type))
                return true;

            if (type.GetCustomAttribute<DistributedNotificationAttribute>() is not null)
                return true;

            foreach (var baseType in self.IncludedAssignableTo)
            {
                if (baseType.IsAssignableFrom(type))
                    return true;
            }

            if (self.MessageFilter is not null)
                return self.MessageFilter(type);

            return self.IncludeAllNotifications && typeof(INotification).IsAssignableFrom(type);
        }, this);
    }

    internal bool HasDynamicRules => IncludeAllNotifications || MessageFilter is not null || IncludedAssignableTo.Count > 0;

    /// <summary>
    /// The notification types resolved at registration as distributed. Types matched only by a dynamic
    /// rule at publish time are not listed here.
    /// </summary>
    public IReadOnlyList<Type> ResolvedTypes { get; internal set; } = [];

    internal string EffectiveTopic =>
        string.IsNullOrEmpty(ResourcePrefix) ? Topic : $"{ResourcePrefix}-{Topic}";
}
