using Foundatio.Mediator;

namespace ConsoleSample.Messages;

public record Order(string OrderId, string CustomerId, decimal Amount, string ProductName);

public record OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    decimal Amount,
    string ProductName
) : INotification;

public record CreateOrder(string OrderId, string CustomerId, decimal Amount, string ProductName) : ICommand;
