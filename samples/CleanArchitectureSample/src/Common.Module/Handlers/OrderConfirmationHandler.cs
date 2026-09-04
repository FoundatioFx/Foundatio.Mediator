using Common.Module.Events;
using Common.Module.Services;
using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.Logging;

namespace Common.Module.Handlers;

/// <summary>
/// Shares the <c>order-created</c> queue with <c>OrderFulfillmentHandler</c> in the Orders module. Publishing
/// one <see cref="OrderCreated"/> sends one message; the worker runs both handlers from it.
/// </summary>
[Queue(QueueName = "order-created", Group = "events", Description = "One OrderCreated message; confirmation and fulfillment both run from it")]
public class OrderConfirmationHandler(INotificationService notificationService, HostInfo host, ILogger<OrderConfirmationHandler> logger)
{
    public async Task HandleAsync(OrderCreated evt, TenantContext tenant, CancellationToken ct)
    {
        logger.LogInformation("Sending order confirmation for {OrderId} (tenant {Tenant}) on {HostId}", evt.OrderId, tenant, host.HostId);

        await notificationService.SendAsync(new Notification(
            Id: Guid.NewGuid().ToString(),
            Type: NotificationType.Success,
            Title: "Order Confirmed",
            Message: $"Your order #{evt.OrderId[..8]} for ${evt.Amount:F2} has been confirmed.",
            RecipientId: evt.CustomerId,
            Timestamp: DateTime.UtcNow), ct);
    }
}
