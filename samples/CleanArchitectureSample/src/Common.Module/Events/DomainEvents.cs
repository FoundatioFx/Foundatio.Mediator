using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;

namespace Common.Module.Events;

/// <summary>
/// Every order lifecycle event. <c>OrderAuditHandler</c> is declared on this interface and receives all of
/// them through one queue, and the notification bus includes them by rule rather than one marker per record.
/// </summary>
public interface IOrderEvent : IDispatchToClient
{
    string OrderId { get; }
}

public record OrderCreated(string OrderId, string CustomerId, decimal Amount, DateTime CreatedAt) : IOrderEvent;
public record OrderUpdated(string OrderId, decimal Amount, string Status, DateTime UpdatedAt) : IOrderEvent;
public record OrderShipped(string OrderId, string Carrier, string TrackingNumber, DateTime ShippedAt) : IOrderEvent;
public record OrderDeleted(string OrderId, DateTime DeletedAt) : IOrderEvent;

// Product events keep the marker interface: every node caches products locally and must invalidate on each change.
public record ProductCreated(string ProductId, string Name, decimal Price, DateTime CreatedAt) : IDistributedNotification, IDispatchToClient;
public record ProductUpdated(string ProductId, string Name, decimal Price, string Status, DateTime UpdatedAt) : IDistributedNotification, IDispatchToClient;
public record ProductDeleted(string ProductId, DateTime DeletedAt) : IDistributedNotification, IDispatchToClient;

// Excluded from the bus in Program.cs: only queued handlers consume it, and the enqueue happens on the publishing node.
public record ProductStockChanged(string ProductId, int OldQuantity, int NewQuantity, DateTime ChangedAt) : INotification;

// Published by workers so the API nodes' event feeds can show which host did the work.
public record DemoJobCompleted(string JobId, string QueueName, string HostId, string Tenant) : IDispatchToClient;
public record BankFileGenerated(string Bank, string FileName, string HostId, string JobId, string QueueName) : IDispatchToClient;
public record WebhookDelivered(string Url, int Attempts, string HostId, string JobId, string QueueName) : IDispatchToClient;
