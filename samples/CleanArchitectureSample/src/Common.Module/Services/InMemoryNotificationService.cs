using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Common.Module.Services;

/// <summary>
/// In-memory implementation of INotificationService for demonstration.
/// In production, this would integrate with email, SMS, push notifications, or message queues.
/// </summary>
public class InMemoryNotificationService(ILogger<InMemoryNotificationService> logger) : INotificationService
{
    private readonly ConcurrentQueue<Notification> _notifications = new();
    private const int MaxNotifications = 500;

    public Task SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        _notifications.Enqueue(notification);

        // Log the notification (in production, this would actually send it)
        logger.LogInformation(
            "[{Type}] {Title}: {Message} (Recipient: {Recipient})",
            notification.Type,
            notification.Title,
            notification.Message,
            notification.RecipientId ?? "System");

        // Keep queue bounded
        while (_notifications.Count > MaxNotifications && _notifications.TryDequeue(out _)) { }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Notification>> GetRecentNotificationsAsync(int count = 20, CancellationToken cancellationToken = default)
    {
        var notifications = _notifications
            .OrderByDescending(n => n.Timestamp)
            .Take(count)
            .ToList();

        return Task.FromResult<IReadOnlyList<Notification>>(notifications);
    }
}
