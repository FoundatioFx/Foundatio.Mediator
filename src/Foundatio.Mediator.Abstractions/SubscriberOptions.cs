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
}
