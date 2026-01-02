using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Benchmarks.Handlers.ImmediateHandlers;

// Scenario 1: Command handler (InvokeAsync without response)
[Immediate.Handlers.Shared.Handler]
public sealed partial class ImmediateHandlersCommandHandler
{
    private async ValueTask Handle(PingCommand request, CancellationToken cancellationToken)
    {
        // Simulate minimal work
        await ValueTask.CompletedTask;
    }
}

// Scenario 2: Query handler (InvokeAsync<T>) - No DI for baseline comparison
[Immediate.Handlers.Shared.Handler]
public sealed partial class ImmediateHandlersQueryHandler
{
    private async ValueTask<Order> Handle(GetOrder request, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return new Order(request.Id, 99.99m, DateTime.UtcNow);
    }
}

// Scenario 3: Event handlers (PublishAsync with multiple handlers)
public sealed class Publisher<TNotification>(
    IServiceProvider serviceProvider
)
{
    public async ValueTask Publish(TNotification notification, CancellationToken token = default)
    {
        foreach (var handler in serviceProvider.GetServices<Immediate.Handlers.Shared.IHandler<TNotification, ValueTuple>>())
        {
            await handler.HandleAsync(notification, token);
        }
    }
}

[Immediate.Handlers.Shared.Handler]
public sealed partial class ImmediateHandlersEventHandler1
{
    private async ValueTask Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Simulate minimal event handling work
        await ValueTask.CompletedTask;
    }
}

[Immediate.Handlers.Shared.Handler]
public sealed partial class ImmediateHandlersEventHandler2
{
    private async ValueTask Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Simulate minimal event handling work
        await ValueTask.CompletedTask;
    }
}

// Scenario 4: InvokeAsync<T> with DI (Query with dependency injection and middleware)
public sealed class TimingBehavior<TRequest, TResponse> : Immediate.Handlers.Shared.Behavior<TRequest, TResponse>
    where TRequest : GetFullQuery
{
    public override async ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return await Next(request, cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
        }
    }
}

[Immediate.Handlers.Shared.Handler]
[Immediate.Handlers.Shared.Behaviors(typeof(TimingBehavior<,>))]
public sealed partial class ImmediateHandlersFullQuery(IOrderService orderService)
{
    private async ValueTask<Order> Handle(GetFullQuery request, CancellationToken cancellationToken)
    {
        return await orderService.GetOrderAsync(request.Id, cancellationToken);
    }
}

// Scenario 5: Cascading messages - IH requires manual publish of events
[Immediate.Handlers.Shared.Handler]
public sealed partial class ImmediateHandlersCreateOrderConsumer(Publisher<OrderCreatedEvent> publisher)
{
    private async ValueTask<Order> HandleAsync(CreateOrder createOrder, CancellationToken token)
    {
        var order = new Order(1, createOrder.Amount, DateTime.UtcNow);
        await publisher.Publish(new OrderCreatedEvent(order.Id, createOrder.CustomerId), token);

        return order;
    }
}

// Handlers for the cascaded OrderCreatedEvent
[Immediate.Handlers.Shared.Handler]
public sealed partial class ImmediateHandlersCreatedConsumer1
{
    private async ValueTask HandleAsync(OrderCreatedEvent _, CancellationToken token)
    {
        // First handler for order created event
        await Task.CompletedTask;
    }
}

[Immediate.Handlers.Shared.Handler]
public sealed partial class ImmediateHandlersCreatedConsumer2
{
    private async ValueTask HandleAsync(OrderCreatedEvent _, CancellationToken token)
    {
        // Second handler for order created event
        await Task.CompletedTask;
    }
}

// Scenario 6: Short-circuit handler (never actually called due to ShortCircuitMiddleware)
public class ShortCircuitBehavior<TRequest, TResponse> : Immediate.Handlers.Shared.Behavior<TRequest, TResponse>
    where TRequest : GetCachedOrder
    where TResponse : Order
{
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    public override async ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        // Always short-circuit with cached result - simulates cache hit scenario
        return (TResponse)_cachedOrder;
    }
}

[Immediate.Handlers.Shared.Handler]
[Immediate.Handlers.Shared.Behaviors(typeof(ShortCircuitBehavior<,>))]
public sealed partial class ImmediateHandlersShortCircuitHandler
{
    private async ValueTask<Order> Handle(GetCachedOrder request, CancellationToken cancellationToken = default)
    {
        // This should never be called - middleware short-circuits before reaching handler
        throw new InvalidOperationException("Short-circuit middleware should have prevented this call");
    }
}
