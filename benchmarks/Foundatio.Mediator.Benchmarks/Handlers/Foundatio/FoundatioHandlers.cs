using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;

namespace Foundatio.Mediator.Benchmarks.Handlers.Foundatio;

// Scenario 1: Command handler (InvokeAsync without response)
[Handler]
public class FoundatioCommandHandler
{
    public Task HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
    {
        // Simulate minimal work - no async state machine
        return Task.CompletedTask;
    }
}

// Scenario 2: Query handler (InvokeAsync<T>)
[Handler]
public class FoundatioQueryHandler
{
    public ValueTask<Order> HandleAsync(GetOrder query, CancellationToken cancellationToken = default)
    {
        return new ValueTask<Order>(new Order(query.Id, 99.99m, DateTime.UtcNow));
    }
}

// Scenario 3: Event handlers (PublishAsync with multiple handlers)
[Handler]
public class FoundatioEventHandler
{
    public Task HandleAsync(UserRegisteredEvent notification, CancellationToken cancellationToken = default)
    {
        // Simulate minimal event handling work - returns completed task with no allocation
        return Task.CompletedTask;
    }
}

[Handler]
public class FoundatioSecondEventHandler
{
    public Task HandleAsync(UserRegisteredEvent notification, CancellationToken cancellationToken = default)
    {
        // Second handler listening for the same event
        return Task.CompletedTask;
    }
}

// Scenario 4: Query handler with dependency injection
[Handler]
public class FoundatioFullQueryHandler
{
    private readonly IOrderService _orderService;

    public FoundatioFullQueryHandler(IOrderService orderService)
    {
        _orderService = orderService;
    }

    public async Task<Order> HandleAsync(GetFullQuery query, CancellationToken cancellationToken = default)
    {
        return await _orderService.GetOrderAsync(query.Id, cancellationToken);
    }
}

// Scenario 5: Cascading messages - returns tuple with result + events that auto-publish
[Handler]
public class FoundatioCreateOrderHandler
{
    public (Order order, OrderCreatedEvent evt) HandleAsync(CreateOrder command, CancellationToken cancellationToken = default)
    {
        // No async state machine needed
        var order = new Order(1, command.Amount, DateTime.UtcNow);
        return (order, new OrderCreatedEvent(order.Id, command.CustomerId));
    }
}

// Handlers for the cascaded OrderCreatedEvent
[Handler]
public class FoundatioFirstOrderCreatedHandler
{
    public Task HandleAsync(OrderCreatedEvent notification, CancellationToken cancellationToken = default)
    {
        // First handler for order created event - no async state machine
        return Task.CompletedTask;
    }
}

[Handler]
public class FoundatioSecondOrderCreatedHandler
{
    public Task HandleAsync(OrderCreatedEvent notification, CancellationToken cancellationToken = default)
    {
        // Second handler for order created event - no async state machine
        return Task.CompletedTask;
    }
}

// Scenario 6: Short-circuit handler (never actually called due to ShortCircuitMiddleware)
[Handler]
public class FoundatioShortCircuitHandler
{
    public Task<Order> HandleAsync(GetCachedOrder query, CancellationToken cancellationToken = default)
    {
        // This should never be called - middleware short-circuits before reaching handler
        throw new InvalidOperationException("Short-circuit middleware should have prevented this call");
    }
}
