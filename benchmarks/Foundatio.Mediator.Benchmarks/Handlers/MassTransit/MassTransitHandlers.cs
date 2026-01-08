using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;
using MassTransit;

namespace Foundatio.Mediator.Benchmarks.Handlers.MassTransit;

// Scenario 1: Command handler (InvokeAsync without response)
public class MassTransitCommandConsumer : IConsumer<PingCommand>
{
    public Task Consume(ConsumeContext<PingCommand> context)
    {
        // Simulate minimal work
        return Task.CompletedTask;
    }
}

// Scenario 2: Query handler (InvokeAsync<T>) - No DI for baseline comparison
public class MassTransitQueryConsumer : IConsumer<GetOrder>
{
    public Task Consume(ConsumeContext<GetOrder> context)
    {
        return context.RespondAsync(new Order(context.Message.Id, 99.99m, DateTime.UtcNow));
    }
}

// Scenario 3: Event handlers (PublishAsync with multiple handlers)
public class MassTransitEventConsumer : IConsumer<UserRegisteredEvent>
{
    public Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        // Simulate minimal event handling work
        return Task.CompletedTask;
    }
}

public class MassTransitEventConsumer2 : IConsumer<UserRegisteredEvent>
{
    public Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        // Second handler listening for the same event
        return Task.CompletedTask;
    }
}

// Scenario 4: Query handler with dependency injection
public class MassTransitFullQueryConsumer : IConsumer<GetFullQuery>
{
    private readonly IOrderService _orderService;

    public MassTransitFullQueryConsumer(IOrderService orderService)
    {
        _orderService = orderService;
    }

    public async Task Consume(ConsumeContext<GetFullQuery> context)
    {
        var order = await _orderService.GetOrderAsync(context.Message.Id);
        await context.RespondAsync(order);
    }
}

// Scenario 5: Cascading messages - MassTransit requires manual publish of events
public class MassTransitCreateOrderConsumer : IConsumer<CreateOrder>
{
    public async Task Consume(ConsumeContext<CreateOrder> context)
    {
        var order = new Order(1, context.Message.Amount, DateTime.UtcNow);
        await context.Publish(new OrderCreatedEvent(order.Id, context.Message.CustomerId));
        await context.RespondAsync(order);
    }
}

// Handlers for the cascaded OrderCreatedEvent
public class MassTransitOrderCreatedConsumer1 : IConsumer<OrderCreatedEvent>
{
    public Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        // First handler for order created event
        return Task.CompletedTask;
    }
}

public class MassTransitOrderCreatedConsumer2 : IConsumer<OrderCreatedEvent>
{
    public Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        // Second handler for order created event
        return Task.CompletedTask;
    }
}

// Scenario 6: Short-circuit handler - MassTransit uses a filter to short-circuit before reaching the consumer.
public class MassTransitShortCircuitConsumer : IConsumer<GetCachedOrder>
{
    public Task Consume(ConsumeContext<GetCachedOrder> context)
    {
        // This should never be called - filter short-circuits before reaching consumer
        throw new InvalidOperationException("Short-circuit filter should have prevented this call");
    }
}

// MassTransit short-circuit filter - returns cached value without calling the consumer
// Must be generic for UseConsumeFilter registration, but only short-circuits GetCachedOrder
public class MassTransitShortCircuitFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        // Only short-circuit for GetCachedOrder messages
        if (context.Message is GetCachedOrder)
        {
            // Short-circuit by responding with cached value - never calls next()
            await context.NotifyConsumed(context.ReceiveContext.ElapsedTime, TypeCache<MassTransitShortCircuitFilter<T>>.ShortName);
            await context.RespondAsync(_cachedOrder);
            return;
        }

        // Pass through for all other message types
        await next.Send(context);
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("short-circuit");
    }
}

// MassTransit timing filter for FullQuery benchmark (equivalent to Foundatio's TimingMiddleware)
public class MassTransitTimingFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await next.Send(context);
        }
        finally
        {
            stopwatch.Stop();
            // In real middleware, you'd log here - we just stop the timer for the benchmark
        }
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("timing");
    }
}
