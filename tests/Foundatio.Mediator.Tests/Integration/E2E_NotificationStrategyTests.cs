using Foundatio.Mediator.Tests.Fixtures;
using Foundatio.Mediator.Generated;
using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

/// <summary>
/// E2E tests for notification publish strategies.
/// The test project uses the default ForeachAwait strategy (via assembly-level MediatorConfiguration).
/// TaskWhenAll and FireAndForget strategies require different assembly-level configuration and are
/// verified at the generator level in <see cref="PublishInterceptorGenerationTests"/>.
/// </summary>
public class E2E_NotificationStrategyTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    private static void ClearAllCaches()
    {
        Mediator.ClearCache();
        PublishInterceptors.ClearCache();
    }

    #region ForeachAwait - All Handlers Run

    public record StrategyTestEvent(string Name);

    [Handler(Order = 1, Lifetime = MediatorLifetime.Singleton)]
    public class FirstStrategyHandler(EventTracker tracker)
    {
        public void Handle(StrategyTestEvent evt) => tracker.Record("first");
    }

    [Handler(Order = 2, Lifetime = MediatorLifetime.Singleton)]
    public class ThrowingStrategyHandler(EventTracker tracker)
    {
        public async Task HandleAsync(StrategyTestEvent evt, CancellationToken ct)
        {
            tracker.Record("throwing");
            await Task.CompletedTask;
            throw new InvalidOperationException("handler-error");
        }
    }

    [Handler(Order = 3, Lifetime = MediatorLifetime.Singleton)]
    public class LastStrategyHandler(EventTracker tracker)
    {
        public void Handle(StrategyTestEvent evt) => tracker.Record("last");
    }

    [Fact]
    public async Task ForeachAwait_AllHandlersRun_EvenWhenOneThrows()
    {
        ClearAllCaches();

        var services = new ServiceCollection();
        services.AddSingleton<EventTracker>();
        services.AddMediator(b => b.AddAssembly<StrategyTestEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var tracker = provider.GetRequiredService<EventTracker>();

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => mediator.PublishAsync(new StrategyTestEvent("test"), TestCancellationToken).AsTask());

        // All three handlers should have executed despite the middle one throwing
        Assert.Equal(3, tracker.Events.Count);
        Assert.Equal("first", tracker.Events[0]);
        Assert.Equal("throwing", tracker.Events[1]);
        Assert.Equal("last", tracker.Events[2]);

        // The AggregateException should contain the handler's exception
        Assert.Single(ex.InnerExceptions);
        Assert.IsType<InvalidOperationException>(ex.InnerExceptions[0]);
        Assert.Equal("handler-error", ex.InnerExceptions[0].Message);
    }

    #endregion

    #region ForeachAwait - Multiple Exceptions Aggregated

    public record MultiErrorEvent(string Name);

    [Handler(Order = 1, Lifetime = MediatorLifetime.Singleton)]
    public class ErrorHandler1(EventTracker tracker)
    {
        public async Task HandleAsync(MultiErrorEvent evt, CancellationToken ct)
        {
            tracker.Record("error1");
            await Task.CompletedTask;
            throw new InvalidOperationException("error-1");
        }
    }

    [Handler(Order = 2, Lifetime = MediatorLifetime.Singleton)]
    public class ErrorHandler2(EventTracker tracker)
    {
        public async Task HandleAsync(MultiErrorEvent evt, CancellationToken ct)
        {
            tracker.Record("error2");
            await Task.CompletedTask;
            throw new ArgumentException("error-2");
        }
    }

    [Handler(Order = 3, Lifetime = MediatorLifetime.Singleton)]
    public class SuccessHandler(EventTracker tracker)
    {
        public void Handle(MultiErrorEvent evt) => tracker.Record("success");
    }

    [Fact]
    public async Task ForeachAwait_AggregatesMultipleExceptions()
    {
        ClearAllCaches();

        var services = new ServiceCollection();
        services.AddSingleton<EventTracker>();
        services.AddMediator(b => b.AddAssembly<MultiErrorEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var tracker = provider.GetRequiredService<EventTracker>();

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => mediator.PublishAsync(new MultiErrorEvent("test"), TestCancellationToken).AsTask());

        // All handlers ran
        Assert.Equal(3, tracker.Events.Count);

        // AggregateException contains both errors
        Assert.Equal(2, ex.InnerExceptions.Count);
        Assert.Contains(ex.InnerExceptions, e => e is InvalidOperationException && e.Message == "error-1");
        Assert.Contains(ex.InnerExceptions, e => e is ArgumentException && e.Message == "error-2");
    }

    #endregion

    #region ForeachAwait - No Exception When All Succeed

    public record SuccessEvent(string Name);

    [Handler(Order = 1, Lifetime = MediatorLifetime.Singleton)]
    public class SuccessEventHandler1(EventTracker tracker)
    {
        public void Handle(SuccessEvent evt) => tracker.Record("success1");
    }

    [Handler(Order = 2, Lifetime = MediatorLifetime.Singleton)]
    public class SuccessEventHandler2(EventTracker tracker)
    {
        public void Handle(SuccessEvent evt) => tracker.Record("success2");
    }

    [Fact]
    public async Task ForeachAwait_NoException_WhenAllHandlersSucceed()
    {
        ClearAllCaches();

        var services = new ServiceCollection();
        services.AddSingleton<EventTracker>();
        services.AddMediator(b => b.AddAssembly<SuccessEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var tracker = provider.GetRequiredService<EventTracker>();

        // Should not throw
        await mediator.PublishAsync(new SuccessEvent("test"), TestCancellationToken);

        Assert.Equal(2, tracker.Events.Count);
        Assert.Equal("success1", tracker.Events[0]);
        Assert.Equal("success2", tracker.Events[1]);
    }

    #endregion
}

