using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;
using MassTransit;

namespace Foundatio.Mediator.Benchmarks.Handlers.MassTransit;

// Scenario 1: Command handler (InvokeAsync without response)
public class MassTransitCommandConsumer : IConsumer<PingCommand>
{
    public async Task Consume(ConsumeContext<PingCommand> context)
    {
        // Simulate minimal work
        await Task.CompletedTask;
    }
}

// Scenario 2: Query handler (InvokeAsync<T>) - No DI for baseline comparison
public class MassTransitQueryConsumer : IConsumer<GetOrder>
{
    public async Task Consume(ConsumeContext<GetOrder> context)
    {
        await Task.CompletedTask;
        await context.RespondAsync(new Order(context.Message.Id, 99.99m, DateTime.UtcNow));
    }
}

// Scenario 3: Single event handler (PublishAsync with single handler)
public class MassTransitEventConsumer : IConsumer<UserRegisteredEvent>
{
    public async Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        // Simulate minimal event handling work
        await Task.CompletedTask;
    }
}

// Scenario 4: Query handler with dependency injection
public class MassTransitQueryWithDependenciesConsumer : IConsumer<GetOrderWithDependencies>
{
    private readonly IOrderService _orderService;

    public MassTransitQueryWithDependenciesConsumer(IOrderService orderService)
    {
        _orderService = orderService;
    }

    public async Task Consume(ConsumeContext<GetOrderWithDependencies> context)
    {
        var order = await _orderService.GetOrderAsync(context.Message.Id);
        await context.RespondAsync(order);
    }
}
