namespace Foundatio.Mediator;

/// <summary>
/// Controls how notifications are published to multiple handlers.
/// </summary>
public enum NotificationPublishStrategy
{
    /// <summary>
    /// Handlers are invoked sequentially, awaiting each one before the next.
    /// This is the default and safest option.
    /// </summary>
    ForeachAwait = 0,

    /// <summary>
    /// Handlers are invoked in parallel using <c>Task.WhenAll</c>.
    /// </summary>
    TaskWhenAll = 1,

    /// <summary>
    /// Handlers are invoked without awaiting completion.
    /// </summary>
    FireAndForget = 2
}
