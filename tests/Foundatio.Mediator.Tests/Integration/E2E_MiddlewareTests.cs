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

    #region UseMiddleware Attribute Tests

    public record UseMiddlewareTestCommand(string Value) : ICommand;

    // Custom attribute with [UseMiddleware] applied to it
    [UseMiddleware(typeof(TrackingAttributeMiddleware))]
    public class TrackingAttribute : Attribute { }

    [Middleware(Lifetime = MediatorLifetime.Singleton, ExplicitOnly = true)]
    public class TrackingAttributeMiddleware
    {
        public List<string> Steps { get; } = [];
        public Task BeforeAsync(object m) { Steps.Add("tracking-before"); return Task.CompletedTask; }
        public Task AfterAsync(object m) { Steps.Add("tracking-after"); return Task.CompletedTask; }
    }

    [Tracking]
    public class UseMiddlewareTestCommandHandler
    {
        public Task HandleAsync(UseMiddlewareTestCommand cmd) { return Task.CompletedTask; }
    }

    [Fact]
    public async Task UseMiddlewareAttribute_CustomAttribute_AppliesMiddleware()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TrackingAttributeMiddleware>();
        services.AddMediator(b => b.AddAssembly<UseMiddlewareTestCommandHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var mw = provider.GetRequiredService<TrackingAttributeMiddleware>();

        await mediator.InvokeAsync(new UseMiddlewareTestCommand("test"), TestCancellationToken);

        Assert.Contains("tracking-before", mw.Steps);
        Assert.Contains("tracking-after", mw.Steps);
    }

    #endregion

    #region ExplicitOnly Middleware Tests

    public record ExplicitOnlyTestCommand1(string Value) : ICommand;
    public record ExplicitOnlyTestCommand2(string Value) : ICommand;

    [Middleware(Lifetime = MediatorLifetime.Singleton, ExplicitOnly = true)]
    public class ExplicitOnlyTrackingMiddleware
    {
        public List<string> Steps { get; } = [];
        public Task BeforeAsync(object m) { Steps.Add($"explicit-before:{m.GetType().Name}"); return Task.CompletedTask; }
    }

    [UseMiddleware(typeof(ExplicitOnlyTrackingMiddleware))]
    public class ExplicitOnlyTestCommand1Handler
    {
        public Task HandleAsync(ExplicitOnlyTestCommand1 cmd) { return Task.CompletedTask; }
    }

    // This handler does NOT have [UseMiddleware], so ExplicitOnly middleware should NOT run
    public class ExplicitOnlyTestCommand2Handler
    {
        public Task HandleAsync(ExplicitOnlyTestCommand2 cmd) { return Task.CompletedTask; }
    }

    [Fact]
    public async Task ExplicitOnlyMiddleware_OnlyRunsWhenExplicitlyReferenced()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ExplicitOnlyTrackingMiddleware>();
        services.AddMediator(b => b.AddAssembly<ExplicitOnlyTestCommand1Handler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var mw = provider.GetRequiredService<ExplicitOnlyTrackingMiddleware>();

        // Command 1 has [UseMiddleware] - middleware should run
        await mediator.InvokeAsync(new ExplicitOnlyTestCommand1("test1"), TestCancellationToken);
        Assert.Contains("explicit-before:ExplicitOnlyTestCommand1", mw.Steps);

        mw.Steps.Clear();

        // Command 2 does NOT have [UseMiddleware] - middleware should NOT run
        await mediator.InvokeAsync(new ExplicitOnlyTestCommand2("test2"), TestCancellationToken);
        Assert.Empty(mw.Steps);
    }

    #endregion

    #region ExecuteAsync Middleware Tests

    public record ExecuteTestCommand(string Value) : ICommand<string>;

    [Middleware(Lifetime = MediatorLifetime.Singleton)]
    public class ExecuteTrackingMiddleware
    {
        public List<string> Steps { get; } = [];

        public async ValueTask<object?> ExecuteAsync(ExecuteTestCommand message, HandlerExecutionDelegate next)
        {
            Steps.Add("execute-before");
            var result = await next();
            Steps.Add("execute-after");
            return result;
        }
    }

    public class ExecuteTestCommandHandler
    {
        private readonly ExecuteTrackingMiddleware _mw;
        public ExecuteTestCommandHandler(ExecuteTrackingMiddleware mw) => _mw = mw;

        public Task<string> HandleAsync(ExecuteTestCommand cmd)
        {
            _mw.Steps.Add("handler");
            return Task.FromResult($"Result: {cmd.Value}");
        }
    }

    [Fact]
    public async Task ExecuteAsyncMiddleware_WrapsHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ExecuteTrackingMiddleware>();
        services.AddMediator(b => b.AddAssembly<ExecuteTestCommandHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var mw = provider.GetRequiredService<ExecuteTrackingMiddleware>();

        var result = await mediator.InvokeAsync(new ExecuteTestCommand("test"), TestCancellationToken);

        Assert.Equal("Result: test", result);
        Assert.Equal(["execute-before", "handler", "execute-after"], mw.Steps);
    }

    public record ExecuteWithInfoTestCommand(string Value) : ICommand;

    [Middleware(Lifetime = MediatorLifetime.Singleton)]
    public class ExecuteWithHandlerInfoMiddleware
    {
        public List<string> CapturedInfo { get; } = [];

        public async ValueTask<object?> ExecuteAsync(
            ExecuteWithInfoTestCommand message,
            HandlerExecutionDelegate next,
            HandlerExecutionInfo handlerInfo)
        {
            CapturedInfo.Add($"HandlerType:{handlerInfo.HandlerType.Name}");
            CapturedInfo.Add($"MethodName:{handlerInfo.HandlerMethod.Name}");
            return await next();
        }
    }

    public class ExecuteWithInfoTestCommandHandler
    {
        public Task HandleAsync(ExecuteWithInfoTestCommand cmd) { return Task.CompletedTask; }
    }

    [Fact]
    public async Task ExecuteAsyncMiddleware_CanAccessHandlerExecutionInfo()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ExecuteWithHandlerInfoMiddleware>();
        services.AddMediator(b => b.AddAssembly<ExecuteWithInfoTestCommandHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var mw = provider.GetRequiredService<ExecuteWithHandlerInfoMiddleware>();

        await mediator.InvokeAsync(new ExecuteWithInfoTestCommand("test"), TestCancellationToken);

        Assert.Contains("HandlerType:ExecuteWithInfoTestCommandHandler", mw.CapturedInfo);
        Assert.Contains("MethodName:HandleAsync", mw.CapturedInfo);
    }

    public record ExecuteExplicitTestCommand1(string Value) : ICommand;
    public record ExecuteExplicitTestCommand2(string Value) : ICommand;

    [Middleware(Lifetime = MediatorLifetime.Singleton, ExplicitOnly = true)]
    public class ExplicitExecuteMiddleware
    {
        public List<string> Steps { get; } = [];

        public async ValueTask<object?> ExecuteAsync(object message, HandlerExecutionDelegate next)
        {
            Steps.Add($"explicit-execute:{message.GetType().Name}");
            return await next();
        }
    }

    [UseMiddleware(typeof(ExplicitExecuteMiddleware))]
    public class ExecuteExplicitTestCommand1Handler
    {
        public Task HandleAsync(ExecuteExplicitTestCommand1 cmd) { return Task.CompletedTask; }
    }

    public class ExecuteExplicitTestCommand2Handler
    {
        public Task HandleAsync(ExecuteExplicitTestCommand2 cmd) { return Task.CompletedTask; }
    }

    [Fact]
    public async Task ExecuteAsyncMiddleware_ExplicitOnly_OnlyRunsWhenReferenced()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ExplicitExecuteMiddleware>();
        services.AddMediator(b => b.AddAssembly<ExecuteExplicitTestCommand1Handler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var mw = provider.GetRequiredService<ExplicitExecuteMiddleware>();

        // Command 1 has [UseMiddleware] - middleware should run
        await mediator.InvokeAsync(new ExecuteExplicitTestCommand1("test1"), TestCancellationToken);
        Assert.Contains("explicit-execute:ExecuteExplicitTestCommand1", mw.Steps);

        mw.Steps.Clear();

        // Command 2 does NOT have [UseMiddleware] - middleware should NOT run
        await mediator.InvokeAsync(new ExecuteExplicitTestCommand2("test2"), TestCancellationToken);
        Assert.Empty(mw.Steps);
    }

    #endregion

}
