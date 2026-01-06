using System.Diagnostics;
using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;

namespace Foundatio.Mediator.Benchmarks.Handlers.Foundatio;

// Scenario 1: Command handler (InvokeAsync without response)
[Handler]
public class FoundatioCommandHandler
{
    public ValueTask HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
    {
        // Simulate minimal work
        return default;
    }
}

// Scenario 2: Query handler (InvokeAsync<T>)
[Handler]
public class FoundatioQueryHandler
{
    public ValueTask<Order> HandleAsync(GetOrder query, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new Order(query.Id, 99.99m, DateTime.UtcNow));
    }
}

// Scenario 3: Event handlers (PublishAsync with multiple handlers)
[Handler]
public class FoundatioEventHandler
{
    public ValueTask HandleAsync(UserRegisteredEvent notification, CancellationToken cancellationToken = default)
    {
        // Simulate minimal event handling work
        return default;
    }
}

[Handler]
public class FoundatioSecondEventHandler
{
    public ValueTask HandleAsync(UserRegisteredEvent notification, CancellationToken cancellationToken = default)
    {
        // Second handler listening for the same event
        return default;
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

    public ValueTask<Order> HandleAsync(GetFullQuery query, CancellationToken cancellationToken = default)
    {
        return _orderService.GetOrderAsync(query.Id, cancellationToken);
    }
}

// Scenario 5: Cascading messages - returns tuple with result + events that auto-publish
[Handler]
public class FoundatioCreateOrderHandler
{
    public ValueTask<(Order order, OrderCreatedEvent evt)> HandleAsync(CreateOrder command, CancellationToken cancellationToken = default)
    {
        var order = new Order(1, command.Amount, DateTime.UtcNow);
        return ValueTask.FromResult((order, new OrderCreatedEvent(order.Id, command.CustomerId)));
    }
}

// Handlers for the cascaded OrderCreatedEvent
[Handler]
public class FoundatioFirstOrderCreatedHandler
{
    public ValueTask HandleAsync(OrderCreatedEvent notification, CancellationToken cancellationToken = default)
    {
        // First handler for order created event
        return default;
    }
}

[Handler]
public class FoundatioSecondOrderCreatedHandler
{
    public ValueTask HandleAsync(OrderCreatedEvent notification, CancellationToken cancellationToken = default)
    {
        // Second handler for order created event
        return default;
    }
}

// Scenario 6: Short-circuit handler (never actually called due to ShortCircuitMiddleware)
[Handler]
public class FoundatioShortCircuitHandler
{
    public ValueTask<Order> HandleAsync(GetCachedOrder query, CancellationToken cancellationToken = default)
    {
        // This should never be called - middleware short-circuits before reaching handler
        throw new InvalidOperationException("Short-circuit middleware should have prevented this call");
    }
}

/// <summary>
/// Simple timing middleware for benchmarking - simulates real-world logging/timing middleware.
/// Only applies to GetFullQuery (FullQuery benchmark).
/// </summary>
[Middleware]
public static class TimingMiddleware
{
    public static Stopwatch Before(GetFullQuery message)
    {
        return Stopwatch.StartNew();
    }

    public static void Finally(GetFullQuery message, Stopwatch? stopwatch)
    {
        stopwatch?.Stop();
        // In real middleware, you'd log here - we just stop the timer for the benchmark
    }
}

/// <summary>
/// Short-circuit middleware that immediately returns a cached result without calling the handler.
/// This demonstrates middleware returning early (cache hit, validation success with cached result, etc.)
/// </summary>
[Middleware]
public class ShortCircuitMiddleware
{
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    public ValueTask<HandlerResult> BeforeAsync(GetCachedOrder message)
    {
        // Always short-circuit with cached result - simulates cache hit scenario
        return ValueTask.FromResult(HandlerResult.ShortCircuit(_cachedOrder));
    }
}
