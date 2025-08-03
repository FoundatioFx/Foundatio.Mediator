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

// Scenario 3: Single event handler (PublishAsync with single handler)
public class FoundatioEventHandler
{
    public async Task HandleAsync(UserRegisteredEvent notification, CancellationToken cancellationToken = default)
    {
        // Simulate minimal event handling work
        await Task.CompletedTask;
    }
}

// Scenario 4: Query handler with dependency injection
public class FoundatioQueryWithDependenciesHandler
{
    private readonly IOrderService _orderService;

    public FoundatioQueryWithDependenciesHandler(IOrderService orderService)
    {
        _orderService = orderService;
    }

    public async Task<Order> HandleAsync(GetOrderWithDependencies query, CancellationToken cancellationToken = default)
    {
        return await _orderService.GetOrderAsync(query.Id, cancellationToken);
    }
}
