using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;
using Wolverine;

namespace Foundatio.Mediator.Benchmarks.Handlers.Wolverine;

// Scenario 1: Command handler (InvokeAsync without response)
public class WolverineCommandHandler
{
    public ValueTask Handle(PingCommand command)
    {
        // Simulate minimal work
        return default;
    }
}

// Scenario 2: Query handler (InvokeAsync<T>) - No DI for baseline comparison
public class WolverineQueryHandler
{
    public ValueTask<Order> Handle(GetOrder query)
    {
        return ValueTask.FromResult(new Order(query.Id, 99.99m, DateTime.UtcNow));
    }
}

// Scenario 3: Event handlers (PublishAsync with multiple handlers)
public class WolverineEventHandler
{
    public ValueTask Handle(UserRegisteredEvent notification)
    {
        // Simulate minimal event handling work
        return default;
    }
}

public class WolverineEventHandler2
{
    public ValueTask Handle(UserRegisteredEvent notification)
    {
        // Second handler listening for the same event
        return default;
    }
}

// Scenario 4: Query handler with dependency injection
public class WolverineFullQueryHandler
{
    public ValueTask<Order> Handle(GetFullQuery query, IOrderService orderService)
    {
        return orderService.GetOrderAsync(query.Id);
    }
}

// Scenario 5: Cascading messages - Wolverine supports cascading via return values
public class WolverineCreateOrderHandler
{
    public ValueTask<(Order, OrderCreatedEvent)> Handle(CreateOrder command)
    {
        var order = new Order(1, command.Amount, DateTime.UtcNow);
        return ValueTask.FromResult((order, new OrderCreatedEvent(order.Id, command.CustomerId)));
    }
}

// Handlers for the cascaded OrderCreatedEvent
public class WolverineOrderCreatedHandler1
{
    public ValueTask Handle(OrderCreatedEvent notification)
    {
        // First handler for order created event
        return default;
    }
}

public class WolverineOrderCreatedHandler2
{
    public ValueTask Handle(OrderCreatedEvent notification)
    {
        // Second handler for order created event
        return default;
    }
}

// Scenario 6: Short-circuit - Wolverine uses Before middleware with HandlerContinuation.Stop
public class WolverineShortCircuitHandler
{
    public ValueTask<Order> Handle(GetCachedOrder query)
    {
        // This should never be called - middleware short-circuits before reaching handler
        throw new InvalidOperationException("Short-circuit middleware should have prevented this call");
    }
}

// Wolverine short-circuit middleware - uses HandlerContinuation to stop processing
public class WolverineShortCircuitMiddleware
{
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    // Wolverine Before method with async ValueTask tuple return for short-circuit
    public static ValueTask<(HandlerContinuation, Order)> BeforeAsync(GetCachedOrder message)
    {
        // Short-circuit by returning Stop with the cached value
        return ValueTask.FromResult((HandlerContinuation.Stop, _cachedOrder));
    }
}

// Wolverine timing middleware for FullQuery benchmark (equivalent to Foundatio's TimingMiddleware)
public class WolverineTimingMiddleware
{
    public static System.Diagnostics.Stopwatch Before(GetFullQuery message)
    {
        return System.Diagnostics.Stopwatch.StartNew();
    }

    public static void Finally(GetFullQuery message, System.Diagnostics.Stopwatch? stopwatch)
    {
        stopwatch?.Stop();
        // In real middleware, you'd log here - we just stop the timer for the benchmark
    }
}
