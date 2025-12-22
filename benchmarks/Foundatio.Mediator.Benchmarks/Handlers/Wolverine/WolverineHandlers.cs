using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;

namespace Foundatio.Mediator.Benchmarks.Handlers.Wolverine;

// Scenario 1: Command handler (InvokeAsync without response)
[FoundatioIgnore]
public class WolverineCommandHandler
{
    public Task Handle(PingCommand command)
    {
        // Simulate minimal work
        return Task.CompletedTask;
    }
}

// Scenario 2: Query handler (InvokeAsync<T>) - No DI for baseline comparison
[FoundatioIgnore]
public class WolverineQueryHandler
{
    public Task<Order> Handle(GetOrder query)
    {
        return Task.FromResult(new Order(query.Id, 99.99m, DateTime.UtcNow));
    }
}

// Scenario 3: Single event handler (PublishAsync with single handler)
[FoundatioIgnore]
public class WolverineEventHandler
{
    public Task Handle(UserRegisteredEvent notification)
    {
        // Simulate minimal event handling work
        return Task.CompletedTask;
    }
}

// Scenario 4: Query handler with dependency injection
[FoundatioIgnore]
public class WolverineQueryWithDependenciesHandler
{
    public Task<Order> Handle(GetOrderWithDependencies query, IOrderService orderService)
    {
        return orderService.GetOrderAsync(query.Id);
    }
}
