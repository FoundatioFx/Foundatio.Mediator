using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_AdvancedMiddlewareTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    #region Exception Propagation

    public record ThrowingCommand(string Name) : ICommand;

    [Middleware(Lifetime = MediatorLifetime.Singleton)]
    public class ExceptionTrackingMiddleware
    {
        public Exception? CapturedException { get; private set; }
        public bool FinallyWasCalled { get; private set; }

        public void Finally(ThrowingCommand msg, Exception? ex)
        {
            FinallyWasCalled = true;
            CapturedException = ex;
        }
    }

    public class ThrowingCommandHandler
    {
        public Task HandleAsync(ThrowingCommand cmd, CancellationToken ct)
        {
            throw new InvalidOperationException($"Handler error: {cmd.Name}");
        }
    }

    [Fact]
    public async Task ExceptionPropagation_FinallyReceivesException_AndRethrows()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ExceptionTrackingMiddleware>();
        services.AddMediator(b => b.AddAssembly<ThrowingCommandHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var mw = provider.GetRequiredService<ExceptionTrackingMiddleware>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.InvokeAsync(new ThrowingCommand("test"), TestCancellationToken).AsTask());

        Assert.Equal("Handler error: test", ex.Message);
        Assert.True(mw.FinallyWasCalled, "Finally middleware should have been called");
        Assert.NotNull(mw.CapturedException);
        Assert.IsType<InvalidOperationException>(mw.CapturedException);
        Assert.Equal("Handler error: test", mw.CapturedException.Message);
    }

    #endregion

    #region HandlerResult.ShortCircuit

    public record ShortCircuitQuery(string Name) : IQuery;

    [Middleware(Lifetime = MediatorLifetime.Singleton)]
    public class ShortCircuitMiddleware
    {
        public HandlerResult Before(ShortCircuitQuery msg)
        {
            return HandlerResult.ShortCircuit($"short-circuited:{msg.Name}");
        }
    }

    public class ShortCircuitQueryHandler
    {
        public static bool WasCalled { get; set; }

        public string Handle(ShortCircuitQuery query)
        {
            WasCalled = true;
            return $"handled:{query.Name}";
        }
    }

    [Fact]
    public async Task ShortCircuit_SkipsHandler_ReturnsShortCircuitValue()
    {
        ShortCircuitQueryHandler.WasCalled = false;

        var services = new ServiceCollection();
        services.AddSingleton<ShortCircuitMiddleware>();
        services.AddMediator(b => b.AddAssembly<ShortCircuitQueryHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.InvokeAsync<string>(new ShortCircuitQuery("test"), TestCancellationToken);

        Assert.Equal("short-circuited:test", result);
        Assert.False(ShortCircuitQueryHandler.WasCalled, "Handler should not have been called when short-circuited");
    }

    #endregion

    #region Middleware State Passing

    public record StateTestMessage(string Value) : ICommand;

    public record MiddlewareTimestamp(string Label, long Ticks);

    [Middleware(Lifetime = MediatorLifetime.Singleton)]
    public class StateTrackingMiddleware
    {
        public MiddlewareTimestamp? CapturedInBefore { get; private set; }
        public MiddlewareTimestamp? CapturedInAfter { get; private set; }
        public MiddlewareTimestamp? CapturedInFinally { get; private set; }

        public MiddlewareTimestamp Before(StateTestMessage msg)
        {
            CapturedInBefore = new MiddlewareTimestamp(msg.Value, DateTime.UtcNow.Ticks);
            return CapturedInBefore;
        }

        public void After(StateTestMessage msg, MiddlewareTimestamp timestamp)
        {
            CapturedInAfter = timestamp;
        }

        public void Finally(StateTestMessage msg, MiddlewareTimestamp timestamp)
        {
            CapturedInFinally = timestamp;
        }
    }

    public class StateTestHandler
    {
        public Task HandleAsync(StateTestMessage msg, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task StatePassing_BeforeReturnsState_AfterAndFinallyReceiveIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<StateTrackingMiddleware>();
        services.AddMediator(b => b.AddAssembly<StateTestHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var mw = provider.GetRequiredService<StateTrackingMiddleware>();

        await mediator.InvokeAsync(new StateTestMessage("hello"), TestCancellationToken);

        Assert.NotNull(mw.CapturedInBefore);
        Assert.NotNull(mw.CapturedInAfter);
        Assert.NotNull(mw.CapturedInFinally);

        // The exact same object returned from Before should be passed to After and Finally
        Assert.Same(mw.CapturedInBefore, mw.CapturedInAfter);
        Assert.Same(mw.CapturedInBefore, mw.CapturedInFinally);
        Assert.Equal("hello", mw.CapturedInBefore.Label);
    }

    #endregion
}
