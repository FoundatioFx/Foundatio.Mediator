using Foundatio.Mediator.Benchmarks.Messages;
using MassTransit;

namespace Foundatio.Mediator.Benchmarks.Handlers.MassTransit;

// MassTransit handlers using their IConsumer interface, named with different suffix so Foundatio won't pick them up
public class MassTransitPingConsumer : IConsumer<PingCommand>
{
    public async Task Consume(ConsumeContext<PingCommand> context)
    {
        // Simulate minimal work
        await Task.CompletedTask;
    }
}

public class MassTransitGreetingConsumer : IConsumer<GreetingQuery>
{
    public async Task Consume(ConsumeContext<GreetingQuery> context)
    {
        await Task.CompletedTask;
        await context.RespondAsync(new GreetingResponse($"Hello, {context.Message.Name}!"));
    }
}

public class MassTransitCreateOrderConsumer : IConsumer<CreateOrderCommand>
{
    public async Task Consume(ConsumeContext<CreateOrderCommand> context)
    {
        // Simulate order creation
        await Task.CompletedTask;
        await context.RespondAsync(new CreateOrderResponse($"Order-{Guid.NewGuid():N}"));
    }
}

public class MassTransitGetOrderDetailsConsumer : IConsumer<GetOrderDetailsQuery>
{
    public async Task Consume(ConsumeContext<GetOrderDetailsQuery> context)
    {
        // Simulate database lookup
        await Task.CompletedTask;
        await context.RespondAsync(new OrderDetails(
            context.Message.OrderId,
            "Product-123",
            1,
            99.99m,
            "Customer-456",
            DateTime.UtcNow
        ));
    }
}

// Multiple handlers for the same notification (publish scenario)
public class MassTransitUserRegisteredEmailConsumer : IConsumer<UserRegisteredEvent>
{
    public async Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        // Simulate sending email
        await Task.Delay(1, context.CancellationToken);
    }
}

public class MassTransitUserRegisteredAnalyticsConsumer : IConsumer<UserRegisteredEvent>
{
    public async Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        // Simulate analytics tracking
        await Task.Delay(1, context.CancellationToken);
    }
}

public class MassTransitUserRegisteredWelcomeConsumer : IConsumer<UserRegisteredEvent>
{
    public async Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        // Simulate welcome message
        await Task.Delay(1, context.CancellationToken);
    }
}
