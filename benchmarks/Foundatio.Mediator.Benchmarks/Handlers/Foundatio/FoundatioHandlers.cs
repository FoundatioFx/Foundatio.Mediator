using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;

namespace Foundatio.Mediator.Benchmarks.Handlers.Foundatio;

// Scenario 1: Command handler (InvokeAsync without response)
public class FoundatioCommandHandler
{
    public async Task HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
    {
        // Simulate minimal work
        await Task.CompletedTask;
    }
}

// Scenario 2: Query handler (InvokeAsync<T>)
public class FoundatioQueryHandler
{
    public async Task<Order> HandleAsync(GetOrder query, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return new Order(query.Id, 99.99m, DateTime.UtcNow);
    }
}

// Scenario 3: Event handlers (PublishAsync with multiple handlers)
public class FoundatioEventHandler
{
    public async Task HandleAsync(UserRegisteredEvent notification, CancellationToken cancellationToken = default)
    {
        // Simulate minimal event handling work
        await Task.CompletedTask;
    }
}

public class FoundatioEventHandler2
{
    public async Task HandleAsync(UserRegisteredEvent notification, CancellationToken cancellationToken = default)
    {
        // Second handler listening for the same event
        await Task.CompletedTask;
    }
}

// Scenario 4: Query handler with dependency injection
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
public class FoundatioCreateOrderHandler
{
    public async Task<(Order order, OrderCreatedEvent evt)> HandleAsync(CreateOrder command, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        var order = new Order(1, command.Amount, DateTime.UtcNow);
        return (order, new OrderCreatedEvent(order.Id, command.CustomerId));
    }
}

// Handlers for the cascaded OrderCreatedEvent
public class FoundatioOrderCreatedHandler1
{
    public async Task HandleAsync(OrderCreatedEvent notification, CancellationToken cancellationToken = default)
    {
        // First handler for order created event
        await Task.CompletedTask;
    }
}

public class FoundatioOrderCreatedHandler2
{
    public async Task HandleAsync(OrderCreatedEvent notification, CancellationToken cancellationToken = default)
    {
        // Second handler for order created event
        await Task.CompletedTask;
    }
}

// Scenario 6: Short-circuit handler (never actually called due to ShortCircuitMiddleware)
public class FoundatioShortCircuitHandler
{
    public Task<Order> HandleAsync(GetCachedOrder query, CancellationToken cancellationToken = default)
    {
        // This should never be called - middleware short-circuits before reaching handler
        throw new InvalidOperationException("Short-circuit middleware should have prevented this call");
    }
}
