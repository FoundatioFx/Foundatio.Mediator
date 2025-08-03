namespace ConsoleSample.Messages;

// Simple messages
public record Ping(string Text);
public record GetGreeting(string Name);

// Order CRUD messages
public record CreateOrder(string CustomerId, decimal Amount, string Description);
public record GetOrder(string OrderId);
public record UpdateOrder(string OrderId, decimal? Amount, string? Description);
public record DeleteOrder(string OrderId);

// Event messages (for publish pattern)
public record OrderCreated(string OrderId, string CustomerId, decimal Amount, DateTime CreatedAt);
public record OrderUpdated(string OrderId, decimal Amount, DateTime UpdatedAt);
public record OrderDeleted(string OrderId, DateTime DeletedAt);

// Order model
public record Order(string Id, string CustomerId, decimal Amount, string Description, DateTime CreatedAt, DateTime? UpdatedAt = null);
