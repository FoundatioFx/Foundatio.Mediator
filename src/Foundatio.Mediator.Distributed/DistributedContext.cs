namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Ambient context that indicates the current execution scope originated from
/// distributed infrastructure (e.g., a pub/sub bus or remote queue).
/// Middleware such as <see cref="QueueMiddleware"/> checks this to avoid
/// re-enqueueing messages that have already been dispatched through shared infrastructure.
/// </summary>
public static class DistributedContext
{
    private static readonly AsyncLocal<bool> _isNotification = new();

    /// <summary>
    /// Gets whether the current execution scope is processing a notification
    /// received from the distributed bus.
    /// </summary>
    public static bool IsNotification => _isNotification.Value;

    /// <summary>
    /// Enters a notification scope. The returned <see cref="IDisposable"/>
    /// restores the previous value when disposed.
    /// </summary>
    public static IDisposable BeginNotificationScope()
    {
        var previous = _isNotification.Value;
        _isNotification.Value = true;
        return new NotificationScope(previous);
    }

    private sealed class NotificationScope(bool previous) : IDisposable
    {
        public void Dispose() => _isNotification.Value = previous;
    }
}
