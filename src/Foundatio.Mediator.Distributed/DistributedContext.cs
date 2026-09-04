namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Ambient context for a notification that arrived from the distributed bus and is being
/// re-published locally. <see cref="QueueMiddleware"/> uses it to avoid enqueueing that same
/// notification a second time; the originating node already did.
/// </summary>
public static class DistributedContext
{
    private static readonly AsyncLocal<object?> _current = new();

    /// <summary>
    /// The notification currently being re-published from the bus, or <c>null</c>.
    /// </summary>
    public static object? CurrentNotification => _current.Value;

    /// <summary>
    /// Whether the current execution scope is re-publishing a notification received from the bus.
    /// </summary>
    public static bool IsNotification => _current.Value is not null;

    /// <summary>
    /// Whether <paramref name="message"/> is the notification currently being re-published from the bus.
    /// Messages sent from inside that notification's handlers are not, so they are enqueued normally.
    /// </summary>
    public static bool IsInboundNotification(object message) => ReferenceEquals(_current.Value, message);

    /// <summary>
    /// Enters a scope for re-publishing <paramref name="notification"/>. Dispose to restore the previous scope.
    /// </summary>
    public static IDisposable BeginNotificationScope(object notification)
    {
        var previous = _current.Value;
        _current.Value = notification;
        return new NotificationScope(previous);
    }

    private sealed class NotificationScope(object? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
