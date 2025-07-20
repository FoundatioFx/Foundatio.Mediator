using MediatR;

namespace Foundatio.Mediator.Benchmarks.Messages;

// Simple command for basic benchmarking
public record PingCommand(string Id) : IRequest;

// Query with return value
public record GreetingQuery(string Name) : IRequest<string>;

// Response for the greeting query - needed for MassTransit
public record GreetingResponse(string Message);

// Notification for publish scenarios
public record UserRegisteredEvent(string UserId, string Email) : MediatR.INotification;

// Complex command with multiple properties
public record CreateOrderCommand(
    string ProductId,
    int Quantity,
    decimal Price,
    string CustomerId
) : IRequest<string>;

// Response for create order command - needed for MassTransit
public record CreateOrderResponse(string OrderId);

// Complex query with return value
public record GetOrderDetailsQuery(string OrderId) : IRequest<OrderDetails>;

public record OrderDetails(
    string OrderId,
    string ProductId,
    int Quantity,
    decimal Price,
    string CustomerId,
    DateTime CreatedAt
);
