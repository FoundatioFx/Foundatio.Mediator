using Foundatio.Mediator;

namespace Common.Module.Events;

// Order Events - Published by Orders.Module, consumed by cross-cutting handlers
public record OrderCreated(string OrderId, string CustomerId, decimal Amount, DateTime CreatedAt) : INotification;
public record OrderUpdated(string OrderId, decimal Amount, string Status, DateTime UpdatedAt) : INotification;
public record OrderDeleted(string OrderId, DateTime DeletedAt) : INotification;

// Product Events - Published by Products.Module, consumed by cross-cutting handlers
public record ProductCreated(string ProductId, string Name, decimal Price, DateTime CreatedAt) : INotification;
public record ProductUpdated(string ProductId, string Name, decimal Price, string Status, DateTime UpdatedAt) : INotification;
public record ProductDeleted(string ProductId, DateTime DeletedAt) : INotification;
public record ProductStockChanged(string ProductId, int OldQuantity, int NewQuantity, DateTime ChangedAt) : INotification;
