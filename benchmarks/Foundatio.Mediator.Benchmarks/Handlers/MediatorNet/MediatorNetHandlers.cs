using Foundatio.Mediator.Benchmarks.Services;
using MediatorLib = Mediator;

namespace Foundatio.Mediator.Benchmarks.Handlers.MediatorNet;

// Message types for Mediator.SourceGenerator
// Note: We define separate message types because Mediator uses different interfaces than MediatR

public record MediatorNetPingCommand(string Id) : MediatorLib.ICommand;

public record MediatorNetGetOrder(int Id) : MediatorLib.IQuery<Order>;

public record MediatorNetGetFullQuery(int Id) : MediatorLib.IQuery<Order>;

public record MediatorNetUserRegisteredEvent(string UserId, string Email) : MediatorLib.INotification;

public record MediatorNetCreateOrder(int CustomerId, decimal Amount) : MediatorLib.IRequest<Order>;

public record MediatorNetOrderCreatedEvent(int OrderId, int CustomerId) : MediatorLib.INotification;

public record MediatorNetGetCachedOrder(int Id) : MediatorLib.IQuery<Order>;

// Scenario 1: Command handler (InvokeAsync without response)
public class MediatorNetCommandHandler : MediatorLib.ICommandHandler<MediatorNetPingCommand>
{
    public ValueTask<MediatorLib.Unit> Handle(MediatorNetPingCommand command, CancellationToken cancellationToken)
    {
        // Simulate minimal work
        return ValueTask.FromResult(MediatorLib.Unit.Value);
    }
}

// Scenario 2: Query handler (InvokeAsync<T>) - No DI for baseline comparison
public class MediatorNetQueryHandler : MediatorLib.IQueryHandler<MediatorNetGetOrder, Order>
{
    public ValueTask<Order> Handle(MediatorNetGetOrder query, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new Order(query.Id, 99.99m, DateTime.UtcNow));
    }
}

// Scenario 3: Notification handlers (PublishAsync with multiple handlers)
public class MediatorNetEventHandler : MediatorLib.INotificationHandler<MediatorNetUserRegisteredEvent>
{
    public ValueTask Handle(MediatorNetUserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Simulate minimal event handling work
        return ValueTask.CompletedTask;
    }
}

public class MediatorNetEventHandler2 : MediatorLib.INotificationHandler<MediatorNetUserRegisteredEvent>
{
    public ValueTask Handle(MediatorNetUserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Second handler listening for the same event
        return ValueTask.CompletedTask;
    }
}

// Scenario 4: Query handler with dependency injection
public class MediatorNetFullQueryHandler : MediatorLib.IQueryHandler<MediatorNetGetFullQuery, Order>
{
    private readonly IOrderService _orderService;

    public MediatorNetFullQueryHandler(IOrderService orderService)
    {
        _orderService = orderService;
    }

    public ValueTask<Order> Handle(MediatorNetGetFullQuery query, CancellationToken cancellationToken)
    {
        return _orderService.GetOrderAsync(query.Id, cancellationToken);
    }
}

// Scenario 5: Cascading messages - MediatorNet requires manual publish of events
public class MediatorNetCreateOrderHandler : MediatorLib.IRequestHandler<MediatorNetCreateOrder, Order>
{
    private readonly MediatorLib.IMediator _mediator;

    public MediatorNetCreateOrderHandler(MediatorLib.IMediator mediator)
    {
        _mediator = mediator;
    }

    public async ValueTask<Order> Handle(MediatorNetCreateOrder request, CancellationToken cancellationToken)
    {
        var order = new Order(1, request.Amount, DateTime.UtcNow);
        await _mediator.Publish(new MediatorNetOrderCreatedEvent(order.Id, request.CustomerId), cancellationToken);
        return order;
    }
}

// Handlers for the cascaded OrderCreatedEvent
public class MediatorNetOrderCreatedHandler1 : MediatorLib.INotificationHandler<MediatorNetOrderCreatedEvent>
{
    public ValueTask Handle(MediatorNetOrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // First handler for order created event
        return ValueTask.CompletedTask;
    }
}

public class MediatorNetOrderCreatedHandler2 : MediatorLib.INotificationHandler<MediatorNetOrderCreatedEvent>
{
    public ValueTask Handle(MediatorNetOrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // Second handler for order created event
        return ValueTask.CompletedTask;
    }
}

// Scenario 6: Short-circuit handler - MediatorNet uses IPipelineBehavior to short-circuit
public class MediatorNetShortCircuitHandler : MediatorLib.IQueryHandler<MediatorNetGetCachedOrder, Order>
{
    public ValueTask<Order> Handle(MediatorNetGetCachedOrder query, CancellationToken cancellationToken)
    {
        // This should never be called - pipeline behavior short-circuits before reaching handler
        throw new InvalidOperationException("Short-circuit behavior should have prevented this call");
    }
}

// MediatorNet short-circuit behavior - returns cached value without calling handler
public class MediatorNetShortCircuitBehavior : MediatorLib.IPipelineBehavior<MediatorNetGetCachedOrder, Order>
{
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    public ValueTask<Order> Handle(MediatorNetGetCachedOrder message, MediatorLib.MessageHandlerDelegate<MediatorNetGetCachedOrder, Order> next, CancellationToken cancellationToken)
    {
        // Short-circuit by returning cached value - never calls next()
        return ValueTask.FromResult(_cachedOrder);
    }
}

// MediatorNet timing behavior for FullQuery benchmark (equivalent to Foundatio's TimingMiddleware)
public class MediatorNetTimingBehavior : MediatorLib.IPipelineBehavior<MediatorNetGetFullQuery, Order>
{
    public async ValueTask<Order> Handle(MediatorNetGetFullQuery message, MediatorLib.MessageHandlerDelegate<MediatorNetGetFullQuery, Order> next, CancellationToken cancellationToken)
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
