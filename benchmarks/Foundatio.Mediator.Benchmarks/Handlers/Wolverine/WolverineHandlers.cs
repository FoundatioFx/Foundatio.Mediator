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
public class WolverineFullQueryHandler
{
    public Task<Order> Handle(GetFullQuery query, IOrderService orderService)
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

// Scenario 6: Short-circuit - Wolverine uses Before middleware with HandlerContinuation.Stop
[FoundatioIgnore]
public class WolverineShortCircuitHandler
{
    public Task<Order> Handle(GetCachedOrder query)
    {
        // This should never be called - middleware short-circuits before reaching handler
        throw new InvalidOperationException("Short-circuit middleware should have prevented this call");
    }
}

// Wolverine short-circuit middleware - uses HandlerContinuation to stop processing
[FoundatioIgnore]
public class WolverineShortCircuitMiddleware
{
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    // Wolverine Before method with tuple return for short-circuit
    public static (HandlerContinuation, Order) Before(GetCachedOrder message)
    {
        // Short-circuit by returning Stop with the cached value
        return (HandlerContinuation.Stop, _cachedOrder);
    }
}
