using Common.Module.Events;
using Common.Module.Services;
using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.Logging;

namespace Common.Module.Handlers;

/// <summary>
/// Audits product events. Products.Module never sees this handler; it publishes events and subscribers react.
/// Each handler/message pair gets an independent queue, all in the "events" worker group.
/// Order events are audited by <see cref="OrderAuditHandler"/> through the <c>IOrderEvent</c> interface.
/// </summary>
public class AuditEventHandler(IAuditService auditService, ILogger<AuditEventHandler> logger)
{
    // Product events
    [Queue(DisplayName = "Product created audit", Group = "events", Description = "Audit trail for product events")]
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

    [Queue(DisplayName = "Product updated audit", Group = "events", Description = "Audit trail for product events")]
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

    [Queue(DisplayName = "Product deleted audit", Group = "events", Description = "Audit trail for product events")]
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
    [Queue(DisplayName = "Stock change audit", Group = "events", Description = "Audit trail for product events")]
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
