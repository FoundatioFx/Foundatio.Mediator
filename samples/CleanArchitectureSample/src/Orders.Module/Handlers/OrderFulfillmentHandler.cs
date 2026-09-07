using Common.Module;
using Common.Module.Events;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.Logging;
using Orders.Module.Data;
using Orders.Module.Domain;

namespace Orders.Module.Handlers;

/// <summary>
/// Shares the <c>order-created</c> queue with <c>OrderConfirmationHandler</c> in Common.Module: one message,
/// both handlers. Publishing <see cref="OrderShipped"/> from here shows a worker-side publish taking the same
/// path as an API-side one: it is enqueued for <c>OrderAuditHandler</c> and crosses the bus to the event feed.
/// </summary>
[Queue(QueueName = "order-created", Group = "events", Description = "One OrderCreated message; confirmation and fulfillment both run from it")]
public class OrderFulfillmentHandler(IOrderRepository repository, HostInfo host, ILogger<OrderFulfillmentHandler> logger)
{
    public async Task HandleAsync(OrderCreated evt, IMediator mediator, TenantContext tenant, CancellationToken ct)
    {
        // Picking and packing.
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        var order = await repository.GetByIdAsync(evt.OrderId, ct);
        if (order is null || order.Status != OrderStatus.Pending)
        {
            logger.LogInformation("Order {OrderId} is {Status}; nothing to ship", evt.OrderId, order?.Status.ToString() ?? "missing");
            return;
        }

        var shippedAt = DateTime.UtcNow;
        var trackingNumber = $"1Z{Random.Shared.Next(100_000_000, 999_999_999)}";
        await repository.UpdateAsync(order with { Status = OrderStatus.Shipped, UpdatedAt = shippedAt }, ct);

        logger.LogInformation("Order {OrderId} shipped ({TrackingNumber}) for {Tenant} on {HostId}", evt.OrderId, trackingNumber, tenant, host.HostId);

        await mediator.PublishAsync(new OrderShipped(evt.OrderId, "UPS", trackingNumber, shippedAt), ct);
    }
}
