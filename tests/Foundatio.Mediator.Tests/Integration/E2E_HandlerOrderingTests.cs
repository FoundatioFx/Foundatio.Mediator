using Foundatio.Mediator.Generated;
using Foundatio.Mediator.Tests.Fixtures;
using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_HandlerOrderingTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    private readonly ITestOutputHelper _output = output;

    private static void ClearAllCaches()
    {
        // Clear caches to ensure tests don't interfere with each other
        Mediator.ClearCache();
        PublishInterceptors.ClearCache();
    }

    public record OrderTestEvent(string Name);

    // Handler with Order = 1 (should execute first)
    [Handler(Order = 1, Lifetime = MediatorLifetime.Singleton)]
    public class FirstOrderHandler(EventTracker tracker, ILogger<FirstOrderHandler> logger)
    {
        public void Handle(OrderTestEvent evt)
        {
            logger.LogInformation("FirstOrderHandler executing");
            tracker.Record("First");
        }
    }

    // Handler with Order = 2 (should execute second)
    [Handler(Order = 2, Lifetime = MediatorLifetime.Singleton)]
    public class SecondOrderHandler(EventTracker tracker, ILogger<SecondOrderHandler> logger)
    {
        public void Handle(OrderTestEvent evt)
        {
            logger.LogInformation("SecondOrderHandler executing");
            tracker.Record("Second");
        }
    }

    // Handler with Order = 3 (should execute third)
    [Handler(Order = 3, Lifetime = MediatorLifetime.Singleton)]
    public class ThirdOrderHandler(EventTracker tracker, ILogger<ThirdOrderHandler> logger)
    {
        public void Handle(OrderTestEvent evt)
        {
            logger.LogInformation("ThirdOrderHandler executing");
            tracker.Record("Third");
        }
    }

    // Handler with no explicit order (should execute last, default is int.MaxValue)
    [Handler(Lifetime = MediatorLifetime.Singleton)]
    public class DefaultOrderHandler(EventTracker tracker, ILogger<DefaultOrderHandler> logger)
    {
        public void Handle(OrderTestEvent evt)
        {
            logger.LogInformation("DefaultOrderHandler executing");
            tracker.Record("Default");
        }
    }

    [Fact]
    public async Task PublishAsync_ExecutesHandlersInOrder()
    {
        ClearAllCaches();
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddSingleton<EventTracker>();
        services.AddMediator(b => b.AddAssembly<OrderTestEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var tracker = provider.GetRequiredService<EventTracker>();

        await mediator.PublishAsync(new OrderTestEvent("test"), TestCancellationToken);

        // Verify execution order: First (1), Second (2), Third (3), Default (int.MaxValue)
        Assert.Collection(tracker.Events,
            e => Assert.Equal("First", e),
            e => Assert.Equal("Second", e),
            e => Assert.Equal("Third", e),
            e => Assert.Equal("Default", e));
    }

    [Fact]
    public async Task PublishAsync_HandlersWithoutOrder_ExecuteLast()
    {
        ClearAllCaches();
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddSingleton<EventTracker>();
        services.AddMediator(b => b.AddAssembly<OrderTestEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var tracker = provider.GetRequiredService<EventTracker>();

        await mediator.PublishAsync(new OrderTestEvent("test"), TestCancellationToken);

        // Default handler should be last
        Assert.Equal("Default", tracker.Events.Last());
    }

    // Test for named property syntax: [Handler(Order = 5)]
    public record NamedPropertyEvent(string Name);

    [Handler(Order = 10)]
    public class NamedPropertyLowPriorityHandler(EventTracker tracker)
    {
        public void Handle(NamedPropertyEvent evt) => tracker.Record("Low");
    }

    [Handler(Order = 5)]
    public class NamedPropertyHighPriorityHandler(EventTracker tracker)
    {
        public void Handle(NamedPropertyEvent evt) => tracker.Record("High");
    }

    [Fact]
    public async Task PublishAsync_NamedPropertyOrder_RespectsOrder()
    {
        ClearAllCaches();
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddSingleton<EventTracker>();
        services.AddMediator(b => b.AddAssembly<NamedPropertyEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var tracker = provider.GetRequiredService<EventTracker>();

        await mediator.PublishAsync(new NamedPropertyEvent("test"), TestCancellationToken);

        // High priority (Order=5) should execute before Low priority (Order=10)
        Assert.Collection(tracker.Events,
            e => Assert.Equal("High", e),
            e => Assert.Equal("Low", e));
    }

    // Test for constructor argument syntax: [Handler(5)]
    public record ConstructorArgEvent(string Name);

    [Handler(20)]
    public class ConstructorArgLowHandler(EventTracker tracker)
    {
        public void Handle(ConstructorArgEvent evt) => tracker.Record("CtorLow");
    }

    [Handler(10)]
    public class ConstructorArgHighHandler(EventTracker tracker)
    {
        public void Handle(ConstructorArgEvent evt) => tracker.Record("CtorHigh");
    }

    [Fact]
    public async Task PublishAsync_ConstructorArgOrder_RespectsOrder()
    {
        ClearAllCaches();
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddSingleton<EventTracker>();
        services.AddMediator(b => b.AddAssembly<ConstructorArgEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var tracker = provider.GetRequiredService<EventTracker>();

        await mediator.PublishAsync(new ConstructorArgEvent("test"), TestCancellationToken);

        // CtorHigh (Order=10) should execute before CtorLow (Order=20)
        Assert.Collection(tracker.Events,
            e => Assert.Equal("CtorHigh", e),
            e => Assert.Equal("CtorLow", e));
    }
}
