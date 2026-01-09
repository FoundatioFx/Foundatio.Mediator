using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;
using MediatorLib = Mediator;

namespace Foundatio.Mediator.Benchmarks.Handlers.MediatorNet;

// Scenario 1: Command handler (InvokeAsync without response)
public class MediatorNetCommandHandler : MediatorLib.ICommandHandler<PingCommand>
{
    public ValueTask<MediatorLib.Unit> Handle(PingCommand command, CancellationToken cancellationToken)
    {
        // Simulate minimal work
        return ValueTask.FromResult(MediatorLib.Unit.Value);
    }
}

// Scenario 2: Query handler (InvokeAsync<T>) - No DI for baseline comparison
public class MediatorNetQueryHandler : MediatorLib.IQueryHandler<GetOrder, Order>
{
    public ValueTask<Order> Handle(GetOrder query, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new Order(query.Id, 99.99m, DateTime.UtcNow));
    }
}

// Scenario 3: Notification handlers (PublishAsync with multiple handlers)
public class MediatorNetEventHandler : MediatorLib.INotificationHandler<UserRegisteredEvent>
{
    public ValueTask Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Simulate minimal event handling work
        return ValueTask.CompletedTask;
    }
}

public class MediatorNetEventHandler2 : MediatorLib.INotificationHandler<UserRegisteredEvent>
{
    public ValueTask Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Second handler listening for the same event
        return ValueTask.CompletedTask;
    }
}

// Scenario 4: Query handler with dependency injection
public class MediatorNetFullQueryHandler : MediatorLib.IQueryHandler<GetFullQuery, Order>
{
    private readonly IOrderService _orderService;

    public MediatorNetFullQueryHandler(IOrderService orderService)
    {
        _orderService = orderService;
    }

    public ValueTask<Order> Handle(GetFullQuery query, CancellationToken cancellationToken)
    {
        return _orderService.GetOrderAsync(query.Id, cancellationToken);
    }
}

// Scenario 5: Cascading messages - MediatorNet requires manual publish of events
public class MediatorNetCreateOrderHandler : MediatorLib.IRequestHandler<CreateOrder, Order>
{
    private readonly MediatorLib.IMediator _mediator;

    public MediatorNetCreateOrderHandler(MediatorLib.IMediator mediator)
    {
        _mediator = mediator;
    }

    public async ValueTask<Order> Handle(CreateOrder request, CancellationToken cancellationToken)
    {
        var order = new Order(1, request.Amount, DateTime.UtcNow);
        await _mediator.Publish(new OrderCreatedEvent(order.Id, request.CustomerId), cancellationToken);
        return order;
    }
}

// Handlers for the cascaded OrderCreatedEvent
public class MediatorNetOrderCreatedHandler1 : MediatorLib.INotificationHandler<OrderCreatedEvent>
{
    public ValueTask Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // First handler for order created event
        return ValueTask.CompletedTask;
    }
}

public class MediatorNetOrderCreatedHandler2 : MediatorLib.INotificationHandler<OrderCreatedEvent>
{
    public ValueTask Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // Second handler for order created event
        return ValueTask.CompletedTask;
    }
}

// Scenario 6: Short-circuit handler - MediatorNet uses IPipelineBehavior to short-circuit
public class MediatorNetShortCircuitHandler : MediatorLib.IQueryHandler<GetCachedOrder, Order>
{
    public ValueTask<Order> Handle(GetCachedOrder query, CancellationToken cancellationToken)
    {
        // This should never be called - pipeline behavior short-circuits before reaching handler
        throw new InvalidOperationException("Short-circuit behavior should have prevented this call");
    }
}

// MediatorNet short-circuit behavior - returns cached value without calling handler
public class MediatorNetShortCircuitBehavior : MediatorLib.IPipelineBehavior<GetCachedOrder, Order>
{
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    public ValueTask<Order> Handle(GetCachedOrder message, MediatorLib.MessageHandlerDelegate<GetCachedOrder, Order> next, CancellationToken cancellationToken)
    {
        // Short-circuit by returning cached value - never calls next()
        return ValueTask.FromResult(_cachedOrder);
    }
}

// MediatorNet timing behavior for FullQuery benchmark (equivalent to Foundatio's TimingMiddleware)
public class MediatorNetTimingBehavior : MediatorLib.IPipelineBehavior<GetFullQuery, Order>
{
    public async ValueTask<Order> Handle(GetFullQuery message, MediatorLib.MessageHandlerDelegate<GetFullQuery, Order> next, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return await next(message, cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            // In real middleware, you'd log here - we just stop the timer for the benchmark
        }
    }
}
