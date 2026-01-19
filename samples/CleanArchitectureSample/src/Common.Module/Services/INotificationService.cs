namespace Common.Module.Services;

/// <summary>
/// Interface for sending notifications.
/// Demonstrates how domain events can trigger external actions
/// without the originating module knowing about the notification system.
/// </summary>
public interface INotificationService
{
    Task SendAsync(Notification notification, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Notification>> GetRecentNotificationsAsync(int count = 20, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a notification that can be sent to users or systems.
/// </summary>
public record Notification(
    string Id,
    NotificationType Type,
    string Title,
    string Message,
    string? RecipientId,
    DateTime Timestamp,
    bool IsRead = false);

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error,
    OrderUpdate,
    InventoryAlert
}
