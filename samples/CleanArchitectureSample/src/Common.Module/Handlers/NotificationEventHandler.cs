using Common.Module.Events;
using Common.Module.Services;
using Microsoft.Extensions.Logging;

namespace Common.Module.Handlers;

/// <summary>
/// Listens to domain events and sends notifications.
/// This demonstrates how domain events enable business workflows
/// without tight coupling between modules:
/// - Low stock alerts when inventory changes
/// - Order confirmations when orders are created
/// - Status updates when orders change
/// </summary>
public class NotificationEventHandler(INotificationService notificationService, ILogger<NotificationEventHandler> logger)
{
    private const int LowStockThreshold = 10;

    // Order notifications
    public async Task HandleAsync(OrderCreated evt, CancellationToken cancellationToken)
    {
        logger.LogDebug("Sending order confirmation notification for order {OrderId}", evt.OrderId);

        await notificationService.SendAsync(new Notification(
            Id: Guid.NewGuid().ToString(),
            Type: NotificationType.Success,
            Title: "Order Confirmed",
            Message: $"Your order #{evt.OrderId[..8]} for ${evt.Amount:F2} has been confirmed.",
            RecipientId: evt.CustomerId,
            Timestamp: DateTime.UtcNow
        ), cancellationToken);
    }

    public async Task HandleAsync(OrderUpdated evt, CancellationToken cancellationToken)
    {
        logger.LogDebug("Sending order update notification for order {OrderId}", evt.OrderId);

        await notificationService.SendAsync(new Notification(
            Id: Guid.NewGuid().ToString(),
            Type: NotificationType.OrderUpdate,
            Title: "Order Updated",
            Message: $"Order #{evt.OrderId[..8]} has been updated. Status: {evt.Status}",
            RecipientId: null, // Would need to look up customer ID in a real app
            Timestamp: DateTime.UtcNow
        ), cancellationToken);
    }

    // Inventory alerts
    public async Task HandleAsync(ProductStockChanged evt, CancellationToken cancellationToken)
    {
        // Only send alert if stock dropped below threshold
        if (evt.NewQuantity > LowStockThreshold || evt.OldQuantity <= LowStockThreshold)
            return;

        logger.LogWarning("Low stock alert for product {ProductId}: {Quantity} units remaining", evt.ProductId, evt.NewQuantity);

        await notificationService.SendAsync(new Notification(
            Id: Guid.NewGuid().ToString(),
            Type: NotificationType.InventoryAlert,
            Title: "Low Stock Alert",
            Message: $"Product {evt.ProductId[..8]} is running low! Only {evt.NewQuantity} units remaining.",
            RecipientId: "inventory-manager", // Would be configured in a real app
            Timestamp: DateTime.UtcNow
        ), cancellationToken);

        // Send critical alert if completely out of stock
        if (evt.NewQuantity == 0)
        {
            await notificationService.SendAsync(new Notification(
                Id: Guid.NewGuid().ToString(),
                Type: NotificationType.Warning,
                Title: "Out of Stock",
                Message: $"Product {evt.ProductId[..8]} is now OUT OF STOCK!",
                RecipientId: "inventory-manager",
                Timestamp: DateTime.UtcNow
            ), cancellationToken);
        }
    }

    public async Task HandleAsync(ProductCreated evt, CancellationToken cancellationToken)
    {
        logger.LogDebug("Sending new product notification for {ProductName}", evt.Name);

        await notificationService.SendAsync(new Notification(
            Id: Guid.NewGuid().ToString(),
            Type: NotificationType.Info,
            Title: "New Product Added",
            Message: $"New product '{evt.Name}' has been added to the catalog at ${evt.Price:F2}.",
            RecipientId: "catalog-manager",
            Timestamp: DateTime.UtcNow
        ), cancellationToken);
    }
}
