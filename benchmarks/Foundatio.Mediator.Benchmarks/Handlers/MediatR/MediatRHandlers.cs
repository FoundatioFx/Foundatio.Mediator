using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;
using MediatR;

namespace Foundatio.Mediator.Benchmarks.Handlers.MediatR;

// Scenario 1: Command handler (InvokeAsync without response)
[FoundatioIgnore]
public class MediatRCommandHandler : IRequestHandler<PingCommand>
{
    public async Task Handle(PingCommand request, CancellationToken cancellationToken)
    {
        // Simulate minimal work
        await Task.CompletedTask;
    }
}

// Scenario 2: Query handler (InvokeAsync<T>) - No DI for baseline comparison
[FoundatioIgnore]
public class MediatRQueryHandler : IRequestHandler<GetOrder, Order>
{
    public async Task<Order> Handle(GetOrder request, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return new Order(request.Id, 99.99m, DateTime.UtcNow);
    }
}

// Scenario 3: Event handlers (PublishAsync with multiple handlers)
[FoundatioIgnore]
public class MediatREventHandler : INotificationHandler<UserRegisteredEvent>
{
    public async Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Simulate minimal event handling work
        await Task.CompletedTask;
    }
}

[FoundatioIgnore]
public class MediatREventHandler2 : INotificationHandler<UserRegisteredEvent>
{
    public async Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Second handler listening for the same event
        await Task.CompletedTask;
    }
}

// Scenario 4: Query handler with dependency injection
[FoundatioIgnore]
public class MediatRQueryWithDependenciesHandler : IRequestHandler<GetOrderWithDependencies, Order>
{
    private readonly IOrderService _orderService;

    public MediatRQueryWithDependenciesHandler(IOrderService orderService)
    {
        _orderService = orderService;
    }

    public async Task<Order> Handle(GetOrderWithDependencies request, CancellationToken cancellationToken)
    {
        return await _orderService.GetOrderAsync(request.Id, cancellationToken);
    }
}

// Scenario 5: Cascading messages - MediatR requires manual publish of events
[FoundatioIgnore]
public class MediatRCreateOrderHandler : IRequestHandler<CreateOrder, Order>
{
    private readonly global::MediatR.IMediator _mediator;

    public MediatRCreateOrderHandler(global::MediatR.IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<Order> Handle(CreateOrder request, CancellationToken cancellationToken)
    {
        var order = new Order(1, request.Amount, DateTime.UtcNow);
        await _mediator.Publish(new OrderCreatedEvent(order.Id, request.CustomerId), cancellationToken);
        return order;
    }
}

// Handlers for the cascaded OrderCreatedEvent
[FoundatioIgnore]
public class MediatROrderCreatedHandler1 : INotificationHandler<OrderCreatedEvent>
{
    public async Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // First handler for order created event
        await Task.CompletedTask;
    }
}

[FoundatioIgnore]
public class MediatROrderCreatedHandler2 : INotificationHandler<OrderCreatedEvent>
{
    public async Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // Second handler for order created event
        await Task.CompletedTask;
    }
}

// MediatR pipeline behavior for timing (equivalent to Foundatio's middleware)
[FoundatioIgnore]
public class TimingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return await next();
        }
        finally
        {
            stopwatch.Stop();
            // In real middleware, you'd log here
        }
    }
}

// Scenario 6: Short-circuit handler - MediatR uses IPipelineBehavior to short-circuit
[FoundatioIgnore]
public class MediatRShortCircuitHandler : IRequestHandler<GetCachedOrder, Order>
{
    public Task<Order> Handle(GetCachedOrder request, CancellationToken cancellationToken)
    {
        // This should never be called - pipeline behavior short-circuits before reaching handler
        throw new InvalidOperationException("Short-circuit behavior should have prevented this call");
    }
}

// MediatR short-circuit behavior - returns cached value without calling handler
[FoundatioIgnore]
public class ShortCircuitBehavior : IPipelineBehavior<GetCachedOrder, Order>
{
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    public Task<Order> Handle(GetCachedOrder request, RequestHandlerDelegate<Order> next, CancellationToken cancellationToken)
    {
        // Short-circuit by returning cached value - never calls next()
        return Task.FromResult(_cachedOrder);
    }
}
