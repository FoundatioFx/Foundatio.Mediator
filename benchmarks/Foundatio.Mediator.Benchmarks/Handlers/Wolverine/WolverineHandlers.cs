using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;
using Wolverine;

namespace Foundatio.Mediator.Benchmarks.Handlers.Wolverine;

// Scenario 1: Command handler (InvokeAsync without response)
[FoundatioIgnore]
public class WolverineCommandHandler
{
    public Task Handle(PingCommand command)
    {
        // Simulate minimal work
        return Task.CompletedTask;
    }
}

// Scenario 2: Query handler (InvokeAsync<T>) - No DI for baseline comparison
[FoundatioIgnore]
public class WolverineQueryHandler
{
    public Task<Order> Handle(GetOrder query)
    {
        return Task.FromResult(new Order(query.Id, 99.99m, DateTime.UtcNow));
    }
}

// Scenario 3: Event handlers (PublishAsync with multiple handlers)
[FoundatioIgnore]
public class WolverineEventHandler
{
    public Task Handle(UserRegisteredEvent notification)
    {
        // Simulate minimal event handling work
        return Task.CompletedTask;
    }
}

[FoundatioIgnore]
public class WolverineEventHandler2
{
    public Task Handle(UserRegisteredEvent notification)
    {
        // Second handler listening for the same event
        return Task.CompletedTask;
    }
}

// Scenario 4: Query handler with dependency injection
[FoundatioIgnore]
public class WolverineQueryWithDependenciesHandler
{
    public Task<Order> Handle(GetOrderWithDependencies query, IOrderService orderService)
    {
        return orderService.GetOrderAsync(query.Id);
    }
}

// Scenario 5: Cascading messages - Wolverine supports cascading via return values
[FoundatioIgnore]
public class WolverineCreateOrderHandler
{
    public (Order, OrderCreatedEvent) Handle(CreateOrder command)
    {
        var order = new Order(1, command.Amount, DateTime.UtcNow);
        return (order, new OrderCreatedEvent(order.Id, command.CustomerId));
    }
}

// Handlers for the cascaded OrderCreatedEvent
[FoundatioIgnore]
public class WolverineOrderCreatedHandler1
{
    public Task Handle(OrderCreatedEvent notification)
    {
        // First handler for order created event
        return Task.CompletedTask;
    }
}

[FoundatioIgnore]
public class WolverineOrderCreatedHandler2
{
    public Task Handle(OrderCreatedEvent notification)
    {
        // Second handler for order created event
        return Task.CompletedTask;
    }
}
