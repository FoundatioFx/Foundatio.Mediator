using Common.Module.Events;
using Common.Module.Services;
using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.Logging;

namespace Common.Module.Handlers;

/// <summary>
/// One handler, one queue, every order event. The method is declared on <see cref="IOrderEvent"/>; the worker
/// deserializes each message to its concrete type and dispatches it here because the interface is assignable.
/// </summary>
[Queue(QueueName = "order-events", DisplayName = "Order audit", Group = "events", Description = "Audit trail for every order event, received through the IOrderEvent interface")]
public class OrderAuditHandler(IAuditService auditService, HostInfo host, ILogger<OrderAuditHandler> logger)
{
    public async Task HandleAsync(IOrderEvent evt, TenantContext tenant, CancellationToken ct)
    {
        var eventType = evt.GetType().Name;
        logger.LogInformation("Auditing {EventType} for order {OrderId} (tenant {Tenant}) on {HostId}", eventType, evt.OrderId, tenant, host.HostId);

        await auditService.LogAsync(new AuditEntry(
            Id: Guid.NewGuid().ToString(),
            EventType: eventType,
            EntityType: "Order",
            EntityId: evt.OrderId,
            Description: Describe(evt),
            Timestamp: DateTime.UtcNow,
            Metadata: new Dictionary<string, object?>
            {
                ["Tenant"] = tenant.TenantId,
                ["Host"] = host.HostId
            }), ct);
    }

    private static string Describe(IOrderEvent evt) => evt switch
    {
        OrderCreated e => $"Order created for customer {e.CustomerId} with amount ${e.Amount:F2}",
        OrderUpdated e => $"Order updated: amount ${e.Amount:F2}, status {e.Status}",
        OrderShipped e => $"Order shipped via {e.Carrier}, tracking {e.TrackingNumber}",
        OrderDeleted => "Order deleted",
        _ => evt.GetType().Name
    };
}
