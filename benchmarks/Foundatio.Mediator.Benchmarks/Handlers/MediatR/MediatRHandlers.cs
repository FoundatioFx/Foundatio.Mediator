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

// Scenario 3: Single event handler (PublishAsync with single handler)
[FoundatioIgnore]
public class MediatREventHandler : INotificationHandler<UserRegisteredEvent>
{
    public async Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Simulate minimal event handling work
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
