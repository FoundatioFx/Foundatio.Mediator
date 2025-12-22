using Foundatio.Mediator.Benchmarks.Services;
using MediatorLib = Mediator;

namespace Foundatio.Mediator.Benchmarks.Handlers.MediatorNet;

// Message types for Mediator.SourceGenerator
// Note: We define separate message types because Mediator uses different interfaces than MediatR

[FoundatioIgnore]
public record MediatorNetPingCommand(string Id) : MediatorLib.ICommand;

[FoundatioIgnore]
public record MediatorNetGetOrder(int Id) : MediatorLib.IQuery<Order>;

[FoundatioIgnore]
public record MediatorNetGetOrderWithDependencies(int Id) : MediatorLib.IQuery<Order>;

[FoundatioIgnore]
public record MediatorNetUserRegisteredEvent(string UserId, string Email) : MediatorLib.INotification;

[FoundatioIgnore]
public record MediatorNetCreateOrder(int CustomerId, decimal Amount) : MediatorLib.IRequest<Order>;

[FoundatioIgnore]
public record MediatorNetOrderCreatedEvent(int OrderId, int CustomerId) : MediatorLib.INotification;

[FoundatioIgnore]
public record MediatorNetGetCachedOrder(int Id) : MediatorLib.IQuery<Order>;

// Scenario 1: Command handler (InvokeAsync without response)
[FoundatioIgnore]
public class MediatorNetCommandHandler : MediatorLib.ICommandHandler<MediatorNetPingCommand>
{
    public ValueTask<MediatorLib.Unit> Handle(MediatorNetPingCommand command, CancellationToken cancellationToken)
    {
        // Simulate minimal work
        return ValueTask.FromResult(MediatorLib.Unit.Value);
    }
}

// Scenario 2: Query handler (InvokeAsync<T>) - No DI for baseline comparison
[FoundatioIgnore]
public class MediatorNetQueryHandler : MediatorLib.IQueryHandler<MediatorNetGetOrder, Order>
{
    public ValueTask<Order> Handle(MediatorNetGetOrder query, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new Order(query.Id, 99.99m, DateTime.UtcNow));
    }
}

// Scenario 3: Notification handlers (PublishAsync with multiple handlers)
[FoundatioIgnore]
public class MediatorNetEventHandler : MediatorLib.INotificationHandler<MediatorNetUserRegisteredEvent>
{
    public ValueTask Handle(MediatorNetUserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Simulate minimal event handling work
        return ValueTask.CompletedTask;
    }
}

[FoundatioIgnore]
public class MediatorNetEventHandler2 : MediatorLib.INotificationHandler<MediatorNetUserRegisteredEvent>
{
    public ValueTask Handle(MediatorNetUserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Second handler listening for the same event
        return ValueTask.CompletedTask;
    }
}

// Scenario 4: Query handler with dependency injection
[FoundatioIgnore]
public class MediatorNetQueryWithDependenciesHandler : MediatorLib.IQueryHandler<MediatorNetGetOrderWithDependencies, Order>
{
    private readonly IOrderService _orderService;

    public MediatorNetQueryWithDependenciesHandler(IOrderService orderService)
    {
        _orderService = orderService;
    }

    public async ValueTask<Order> Handle(MediatorNetGetOrderWithDependencies query, CancellationToken cancellationToken)
    {
        return await _orderService.GetOrderAsync(query.Id, cancellationToken);
    }
}

// Scenario 5: Cascading messages - MediatorNet requires manual publish of events
[FoundatioIgnore]
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
[FoundatioIgnore]
public class MediatorNetOrderCreatedHandler1 : MediatorLib.INotificationHandler<MediatorNetOrderCreatedEvent>
{
    public ValueTask Handle(MediatorNetOrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // First handler for order created event
        return ValueTask.CompletedTask;
    }
}

[FoundatioIgnore]
public class MediatorNetOrderCreatedHandler2 : MediatorLib.INotificationHandler<MediatorNetOrderCreatedEvent>
{
    public ValueTask Handle(MediatorNetOrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // Second handler for order created event
        return ValueTask.CompletedTask;
    }
}

// Scenario 6: Short-circuit handler - MediatorNet uses IPipelineBehavior to short-circuit
[FoundatioIgnore]
public class MediatorNetShortCircuitHandler : MediatorLib.IQueryHandler<MediatorNetGetCachedOrder, Order>
{
    public ValueTask<Order> Handle(MediatorNetGetCachedOrder query, CancellationToken cancellationToken)
    {
        // This should never be called - pipeline behavior short-circuits before reaching handler
        throw new InvalidOperationException("Short-circuit behavior should have prevented this call");
    }
}

// MediatorNet short-circuit behavior - returns cached value without calling handler
[FoundatioIgnore]
public class MediatorNetShortCircuitBehavior : MediatorLib.IPipelineBehavior<MediatorNetGetCachedOrder, Order>
{
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    public ValueTask<Order> Handle(MediatorNetGetCachedOrder message, MediatorLib.MessageHandlerDelegate<MediatorNetGetCachedOrder, Order> next, CancellationToken cancellationToken)
    {
        // Short-circuit by returning cached value - never calls next()
        return ValueTask.FromResult(_cachedOrder);
    }
}
