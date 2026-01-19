using Common.Module.Events;
using Common.Module.Services;
using Microsoft.Extensions.Logging;

namespace Common.Module.Handlers;

/// <summary>
/// Listens to domain events from ALL modules and creates audit log entries.
/// This demonstrates Clean Architecture's event-driven loose coupling:
/// - Orders.Module and Products.Module don't know this handler exists
/// - They just publish events; subscribers react independently
/// - Adding new audit capabilities requires no changes to source modules
/// </summary>
public class AuditEventHandler(IAuditService auditService, ILogger<AuditEventHandler> logger)
{
    // Order events
    public async Task HandleAsync(OrderCreated evt, CancellationToken cancellationToken)
    {
        logger.LogDebug("Auditing OrderCreated event for order {OrderId}", evt.OrderId);

        await auditService.LogAsync(new AuditEntry(
            Id: Guid.NewGuid().ToString(),
            EventType: nameof(OrderCreated),
            EntityType: "Order",
            EntityId: evt.OrderId,
            Description: $"Order created for customer {evt.CustomerId} with amount ${evt.Amount:F2}",
            Timestamp: evt.CreatedAt,
            Metadata: new Dictionary<string, object?>
            {
                ["CustomerId"] = evt.CustomerId,
                ["Amount"] = evt.Amount
            }
        ), cancellationToken);
    }

    public async Task HandleAsync(OrderUpdated evt, CancellationToken cancellationToken)
    {
        logger.LogDebug("Auditing OrderUpdated event for order {OrderId}", evt.OrderId);

        await auditService.LogAsync(new AuditEntry(
            Id: Guid.NewGuid().ToString(),
            EventType: nameof(OrderUpdated),
            EntityType: "Order",
            EntityId: evt.OrderId,
            Description: $"Order updated: amount ${evt.Amount:F2}, status {evt.Status}",
            Timestamp: evt.UpdatedAt,
            Metadata: new Dictionary<string, object?>
            {
                ["Amount"] = evt.Amount,
                ["Status"] = evt.Status
            }
        ), cancellationToken);
    }

    public async Task HandleAsync(OrderDeleted evt, CancellationToken cancellationToken)
    {
        logger.LogDebug("Auditing OrderDeleted event for order {OrderId}", evt.OrderId);

        await auditService.LogAsync(new AuditEntry(
            Id: Guid.NewGuid().ToString(),
            EventType: nameof(OrderDeleted),
            EntityType: "Order",
            EntityId: evt.OrderId,
            Description: "Order deleted",
            Timestamp: evt.DeletedAt
        ), cancellationToken);
    }

    // Product events
    public async Task HandleAsync(ProductCreated evt, CancellationToken cancellationToken)
    {
        logger.LogDebug("Auditing ProductCreated event for product {ProductId}", evt.ProductId);

        await auditService.LogAsync(new AuditEntry(
            Id: Guid.NewGuid().ToString(),
            EventType: nameof(ProductCreated),
            EntityType: "Product",
            EntityId: evt.ProductId,
            Description: $"Product '{evt.Name}' created with price ${evt.Price:F2}",
            Timestamp: evt.CreatedAt,
            Metadata: new Dictionary<string, object?>
            {
                ["Name"] = evt.Name,
                ["Price"] = evt.Price
            }
        ), cancellationToken);
    }

    public async Task HandleAsync(ProductUpdated evt, CancellationToken cancellationToken)
    {
        logger.LogDebug("Auditing ProductUpdated event for product {ProductId}", evt.ProductId);

        await auditService.LogAsync(new AuditEntry(
            Id: Guid.NewGuid().ToString(),
            EventType: nameof(ProductUpdated),
            EntityType: "Product",
            EntityId: evt.ProductId,
            Description: $"Product '{evt.Name}' updated: price ${evt.Price:F2}, status {evt.Status}",
            Timestamp: evt.UpdatedAt,
            Metadata: new Dictionary<string, object?>
            {
                ["Name"] = evt.Name,
                ["Price"] = evt.Price,
                ["Status"] = evt.Status
            }
        ), cancellationToken);
    }

    public async Task HandleAsync(ProductDeleted evt, CancellationToken cancellationToken)
    {
        logger.LogDebug("Auditing ProductDeleted event for product {ProductId}", evt.ProductId);

        await auditService.LogAsync(new AuditEntry(
            Id: Guid.NewGuid().ToString(),
            EventType: nameof(ProductDeleted),
            EntityType: "Product",
            EntityId: evt.ProductId,
            Description: "Product deleted",
            Timestamp: evt.DeletedAt
        ), cancellationToken);
    }

    // Stock-specific events
    public async Task HandleAsync(ProductStockChanged evt, CancellationToken cancellationToken)
    {
        logger.LogDebug("Auditing ProductStockChanged event for product {ProductId}", evt.ProductId);

        var direction = evt.NewQuantity > evt.OldQuantity ? "increased" : "decreased";

        await auditService.LogAsync(new AuditEntry(
            Id: Guid.NewGuid().ToString(),
            EventType: nameof(ProductStockChanged),
            EntityType: "Product",
            EntityId: evt.ProductId,
            Description: $"Stock {direction} from {evt.OldQuantity} to {evt.NewQuantity}",
            Timestamp: evt.ChangedAt,
            Metadata: new Dictionary<string, object?>
            {
                ["OldQuantity"] = evt.OldQuantity,
                ["NewQuantity"] = evt.NewQuantity,
                ["Change"] = evt.NewQuantity - evt.OldQuantity
            }
        ), cancellationToken);
    }
}
