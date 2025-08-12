using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_MiddlewareTests
{
    public record E2eCmd(string Name) : ICommand;

    public class TrackingMiddleware
    {
        public List<string> Steps { get; } = new();
        public Task BeforeAsync(E2eCmd m) { Steps.Add("before:" + m.Name); return Task.CompletedTask; }
        public Task AfterAsync(E2eCmd m) { Steps.Add("after:" + m.Name); return Task.CompletedTask; }
        public Task FinallyAsync(E2eCmd m) { Steps.Add("finally:" + m.Name); return Task.CompletedTask; }
    }

    public class E2eCmdHandler
    {
        private readonly TrackingMiddleware _mw;
        public E2eCmdHandler(TrackingMiddleware mw) => _mw = mw;
        public Task HandleAsync(E2eCmd m, CancellationToken ct) { _mw.Steps.Add("handle:" + m.Name); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Middleware_Order_NormalPath()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TrackingMiddleware>();
        services.AddMediator(b => b.AddAssembly<E2eCmdHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var mw = provider.GetRequiredService<TrackingMiddleware>();

        await mediator.InvokeAsync(new E2eCmd("x"));
        Assert.Equal(new[] { "before:x", "handle:x", "after:x", "finally:x" }, mw.Steps);
    }
}
