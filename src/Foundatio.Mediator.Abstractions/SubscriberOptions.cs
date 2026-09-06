using System.Threading.Channels;

namespace Foundatio.Mediator;

/// <summary>
/// Options for configuring a dynamic subscription created via
/// <see cref="IMediator.SubscribeAsync{T}"/>.
/// </summary>
public class SubscriberOptions
{
    /// <summary>
    /// Maximum number of items buffered per subscriber. When full, the behavior is
    /// determined by <see cref="FullMode"/>. Default is 100.
    /// </summary>
    public int MaxCapacity { get; set; } = 100;

    /// <summary>
    /// The behavior when the buffer is full and a new item arrives.
    /// Default is <see cref="BoundedChannelFullMode.DropOldest"/>.
    /// </summary>
    public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.DropOldest;

    /// <summary>
    /// Optional predicate applied to the original notification before buffering it.
    /// Runs synchronously on the publisher; keep it fast and thread safe.
    /// </summary>
    public Func<object, bool>? Filter { get; set; }

    /// <summary>
    /// Called for every item evicted by a DropOldest, DropNewest, or DropWrite policy.
    /// Receives the subscription item (including MessageContext when requested).
    /// Runs synchronously on the publisher; it must be thread safe and must not throw.
    /// </summary>
    public Action<object>? OnDropped { get; set; }
}
