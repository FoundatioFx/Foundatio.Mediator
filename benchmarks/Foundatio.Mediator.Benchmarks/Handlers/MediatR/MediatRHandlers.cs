using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;
using MediatR;

namespace Foundatio.Mediator.Benchmarks.Handlers.MediatR;

// Scenario 1: Command handler (InvokeAsync without response)
public class MediatRCommandHandler : IRequestHandler<PingCommand>
{
    public Task Handle(PingCommand request, CancellationToken cancellationToken)
    {
        // Simulate minimal work - no async state machine
        return Task.CompletedTask;
    }
}

// Scenario 2: Query handler (InvokeAsync<T>) - No DI for baseline comparison
public class MediatRQueryHandler : IRequestHandler<GetOrder, Order>
{
    public Task<Order> Handle(GetOrder request, CancellationToken cancellationToken)
    {
        // No async state machine - return Task.FromResult
        return Task.FromResult(new Order(request.Id, 99.99m, DateTime.UtcNow));
    }
}

// Scenario 3: Event handlers (PublishAsync with multiple handlers)
public class MediatREventHandler : INotificationHandler<UserRegisteredEvent>
{
    public Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Simulate minimal event handling work - no async state machine
        return Task.CompletedTask;
    }
}

public class MediatREventHandler2 : INotificationHandler<UserRegisteredEvent>
{
    public Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Second handler listening for the same event - no async state machine
        return Task.CompletedTask;
    }
}

// Scenario 4: Query handler with dependency injection
public class MediatRFullQueryHandler : IRequestHandler<GetFullQuery, Order>
{
    private readonly IOrderService _orderService;

    public MediatRFullQueryHandler(IOrderService orderService)
    {
        _orderService = orderService;
    }

    public async Task<Order> Handle(GetFullQuery request, CancellationToken cancellationToken)
    {
        return await _orderService.GetOrderAsync(request.Id, cancellationToken);
    }
}

// Scenario 5: Cascading messages - MediatR requires manual publish of events
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
public class MediatROrderCreatedHandler1 : INotificationHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // First handler for order created event - no async state machine
        return Task.CompletedTask;
    }
}

public class MediatROrderCreatedHandler2 : INotificationHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // Second handler for order created event - no async state machine
        return Task.CompletedTask;
    }
}

// MediatR pipeline behavior for timing (equivalent to Foundatio's middleware)
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
public class MediatRShortCircuitHandler : IRequestHandler<GetCachedOrder, Order>
{
    public Task<Order> Handle(GetCachedOrder request, CancellationToken cancellationToken)
    {
        // This should never be called - pipeline behavior short-circuits before reaching handler
        throw new InvalidOperationException("Short-circuit behavior should have prevented this call");
    }
}

// MediatR short-circuit behavior - returns cached value without calling handler
public class ShortCircuitBehavior : IPipelineBehavior<GetCachedOrder, Order>
{
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    public Task<Order> Handle(GetCachedOrder request, RequestHandlerDelegate<Order> next, CancellationToken cancellationToken)
    {
        // Short-circuit by returning cached value - never calls next()
        return Task.FromResult(_cachedOrder);
    }
}
