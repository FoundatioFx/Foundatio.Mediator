using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;

namespace Common.Module.Events;

// Order Events - Published by Orders.Module, consumed by cross-cutting handlers
// IDistributedNotification ensures these fan out to all replicas via SNS/SQS
public record OrderCreated(string OrderId, string CustomerId, decimal Amount, DateTime CreatedAt) : IDistributedNotification, IDispatchToClient;
public record OrderUpdated(string OrderId, decimal Amount, string Status, DateTime UpdatedAt) : IDistributedNotification, IDispatchToClient;
public record OrderDeleted(string OrderId, DateTime DeletedAt) : IDistributedNotification, IDispatchToClient;

// Product Events - Published by Products.Module, consumed by cross-cutting handlers
public record ProductCreated(string ProductId, string Name, decimal Price, DateTime CreatedAt) : IDistributedNotification, IDispatchToClient;
public record ProductUpdated(string ProductId, string Name, decimal Price, string Status, DateTime UpdatedAt) : IDistributedNotification, IDispatchToClient;
public record ProductDeleted(string ProductId, DateTime DeletedAt) : IDistributedNotification, IDispatchToClient;
public record ProductStockChanged(string ProductId, int OldQuantity, int NewQuantity, DateTime ChangedAt) : IDistributedNotification;
