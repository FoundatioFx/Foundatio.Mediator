using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_MiddlewareTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    public record E2eCmd(string Name) : ICommand;

    [Middleware(Lifetime = MediatorLifetime.Singleton)]
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

        await mediator.InvokeAsync(new E2eCmd("x"), TestCancellationToken);
        Assert.Equal(new[] { "before:x", "handle:x", "after:x", "finally:x" }, mw.Steps);
    }

    public interface IValidatable { }
    public record ValidatableCommand(string Value) : IValidatable;

    [Middleware(Lifetime = MediatorLifetime.Singleton)]
    public class InterfaceMiddleware
    {
        public List<string> Steps { get; } = new();
        public Task BeforeAsync(IValidatable m) { Steps.Add("interface-before"); return Task.CompletedTask; }
    }

    public class ValidatableCommandHandler
    {
        public Task HandleAsync(ValidatableCommand cmd) { return Task.CompletedTask; }
    }

    [Fact]
    public async Task Middleware_AppliesTo_MessageInterface()
    {
        var services = new ServiceCollection();
        services.AddSingleton<InterfaceMiddleware>();
        services.AddMediator(b => b.AddAssembly<ValidatableCommandHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var mw = provider.GetRequiredService<InterfaceMiddleware>();

        await mediator.InvokeAsync(new ValidatableCommand("test"), TestCancellationToken);
        Assert.Contains("interface-before", mw.Steps);
    }

    public abstract record BaseCommand(string Id);
    public record DerivedCommand(string Id, string Name) : BaseCommand(Id);

    [Middleware(Lifetime = MediatorLifetime.Singleton)]
    public class BaseClassMiddleware
    {
        public List<string> Steps { get; } = new();
        public Task BeforeAsync(BaseCommand m) { Steps.Add("base-before:" + m.Id); return Task.CompletedTask; }
    }

    public class DerivedCommandHandler
    {
        public Task HandleAsync(DerivedCommand cmd) { return Task.CompletedTask; }
    }

    [Fact]
    public async Task Middleware_AppliesTo_MessageBaseClass()
    {
        var services = new ServiceCollection();
        services.AddSingleton<BaseClassMiddleware>();
        services.AddMediator(b => b.AddAssembly<DerivedCommandHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var mw = provider.GetRequiredService<BaseClassMiddleware>();

        await mediator.InvokeAsync(new DerivedCommand("123", "Test"), TestCancellationToken);
        Assert.Contains("base-before:123", mw.Steps);
    }

    public record HandlerInfoTestCommand(string Value) : ICommand;

    [Middleware(Lifetime = MediatorLifetime.Singleton)]
    public class HandlerInfoMiddleware
    {
        public List<string> CapturedInfo { get; } = new();

        public Task BeforeAsync(HandlerInfoTestCommand cmd, HandlerExecutionInfo handlerInfo)
        {
            CapturedInfo.Add($"HandlerType:{handlerInfo.HandlerType.Name}");
            CapturedInfo.Add($"MethodName:{handlerInfo.HandlerMethod.Name}");
            return Task.CompletedTask;
        }
    }

    public class HandlerInfoTestCommandHandler
    {
        public Task HandleAsync(HandlerInfoTestCommand cmd) { return Task.CompletedTask; }
    }

    [Fact]
    public async Task Middleware_CanAccess_HandlerExecutionInfo()
    {
        var services = new ServiceCollection();
        services.AddSingleton<HandlerInfoMiddleware>();
        services.AddMediator(b => b.AddAssembly<HandlerInfoTestCommandHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var mw = provider.GetRequiredService<HandlerInfoMiddleware>();

        await mediator.InvokeAsync(new HandlerInfoTestCommand("test"), TestCancellationToken);

        Assert.Contains("HandlerType:HandlerInfoTestCommandHandler", mw.CapturedInfo);
        Assert.Contains("MethodName:HandleAsync", mw.CapturedInfo);
    }

    public record HandlerInfoAllPhasesCommand(string Value) : ICommand;

    [Middleware(Lifetime = MediatorLifetime.Singleton)]
    public class HandlerInfoAllPhasesMiddleware
    {
        public List<string> CapturedInfo { get; } = new();

        public Task BeforeAsync(HandlerInfoAllPhasesCommand cmd, HandlerExecutionInfo info)
        {
            CapturedInfo.Add($"Before-{info.HandlerType.Name}-{info.HandlerMethod.Name}");
            return Task.CompletedTask;
        }

        public Task AfterAsync(HandlerInfoAllPhasesCommand cmd, HandlerExecutionInfo info)
        {
            CapturedInfo.Add($"After-{info.HandlerType.Name}-{info.HandlerMethod.Name}");
            return Task.CompletedTask;
        }

        public Task FinallyAsync(HandlerInfoAllPhasesCommand cmd, HandlerExecutionInfo info)
        {
            CapturedInfo.Add($"Finally-{info.HandlerType.Name}-{info.HandlerMethod.Name}");
            return Task.CompletedTask;
        }
    }

    public class HandlerInfoAllPhasesCommandHandler
    {
        public Task HandleAsync(HandlerInfoAllPhasesCommand cmd) { return Task.CompletedTask; }
    }

    [Fact]
    public async Task Middleware_CanAccess_HandlerExecutionInfo_InAllPhases()
    {
        var services = new ServiceCollection();
        services.AddSingleton<HandlerInfoAllPhasesMiddleware>();
        services.AddMediator(b => b.AddAssembly<HandlerInfoAllPhasesCommandHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var mw = provider.GetRequiredService<HandlerInfoAllPhasesMiddleware>();

        await mediator.InvokeAsync(new HandlerInfoAllPhasesCommand("test"), TestCancellationToken);

        Assert.Equal(3, mw.CapturedInfo.Count);
        Assert.Equal("Before-HandlerInfoAllPhasesCommandHandler-HandleAsync", mw.CapturedInfo[0]);
        Assert.Equal("After-HandlerInfoAllPhasesCommandHandler-HandleAsync", mw.CapturedInfo[1]);
        Assert.Equal("Finally-HandlerInfoAllPhasesCommandHandler-HandleAsync", mw.CapturedInfo[2]);
    }

    public record StaticHandlerInfoCommand(string Value) : ICommand;

    public static class StaticHandlerInfoMiddleware
    {
        public static List<string> CapturedInfo { get; } = new();

        public static Task BeforeAsync(StaticHandlerInfoCommand cmd, HandlerExecutionInfo info)
        {
            CapturedInfo.Add($"Static-{info.HandlerType.Name}-{info.HandlerMethod.Name}");
            return Task.CompletedTask;
        }
    }

    public class StaticHandlerInfoCommandHandler
    {
        public Task HandleAsync(StaticHandlerInfoCommand cmd) { return Task.CompletedTask; }
    }

    [Fact]
    public async Task Middleware_CanAccess_HandlerExecutionInfo_StaticMiddleware()
    {
        StaticHandlerInfoMiddleware.CapturedInfo.Clear();

        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<StaticHandlerInfoCommandHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.InvokeAsync(new StaticHandlerInfoCommand("test"), TestCancellationToken);

        Assert.Single(StaticHandlerInfoMiddleware.CapturedInfo);
        Assert.Equal("Static-StaticHandlerInfoCommandHandler-HandleAsync", StaticHandlerInfoMiddleware.CapturedInfo[0]);
    }

}
