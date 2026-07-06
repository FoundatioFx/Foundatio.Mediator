using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_ScopedPerInvokeTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    private readonly ITestOutputHelper _output = output;

    // Scoped dependency used to observe which DI scope a component was resolved from
    public class ScopedProbe : IDisposable
    {
        public Guid InstanceId { get; } = Guid.NewGuid();
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    public record ScopedPerInvokeMessage(string Value);
    public record AmbientMessage(string Value);
    public record CascadeSourceMessage(string Value);
    public record CascadedEvent(string Value);
    public record NestedParentMessage(string Value);
    public record NestedChildMessage(string Value);
    public record NestedIsolatedChildMessage(string Value);
    public record SyncScopedPerInvokeMessage(string Value);

    [Handler(Lifetime = MediatorLifetime.ScopedPerInvoke)]
    public class ScopedPerInvokeHandler(ScopedProbe probe)
    {
        public static List<ScopedProbe> Probes { get; } = [];

        public Task<string> HandleAsync(ScopedPerInvokeMessage msg, CancellationToken ct)
        {
            lock (Probes)
                Probes.Add(probe);
            return Task.FromResult($"Handled: {msg.Value}");
        }
    }

    [Handler(Lifetime = MediatorLifetime.Scoped)]
    public class AmbientHandler(ScopedProbe probe)
    {
        public static List<ScopedProbe> Probes { get; } = [];

        public Task<string> HandleAsync(AmbientMessage msg, CancellationToken ct)
        {
            lock (Probes)
                Probes.Add(probe);
            return Task.FromResult($"Handled: {msg.Value}");
        }
    }

    [Handler(Lifetime = MediatorLifetime.ScopedPerInvoke)]
    public class CascadeSourceHandler(ScopedProbe probe)
    {
        public static List<ScopedProbe> Probes { get; } = [];

        public Task<(string, CascadedEvent)> HandleAsync(CascadeSourceMessage msg, CancellationToken ct)
        {
            lock (Probes)
                Probes.Add(probe);
            return Task.FromResult(($"Handled: {msg.Value}", new CascadedEvent(msg.Value)));
        }
    }

    public class CascadedEventHandler(ScopedProbe probe)
    {
        public static List<ScopedProbe> Probes { get; } = [];

        public Task HandleAsync(CascadedEvent msg, CancellationToken ct)
        {
            lock (Probes)
                Probes.Add(probe);
            return Task.CompletedTask;
        }
    }

    [Handler(Lifetime = MediatorLifetime.ScopedPerInvoke)]
    public class NestedParentHandler(ScopedProbe probe)
    {
        public static List<ScopedProbe> Probes { get; } = [];

        public async Task<string> HandleAsync(NestedParentMessage msg, IMediator mediator, CancellationToken ct)
        {
            lock (Probes)
                Probes.Add(probe);

            await mediator.InvokeAsync(new NestedChildMessage(msg.Value), ct);
            await mediator.InvokeAsync(new NestedIsolatedChildMessage(msg.Value), ct);
            return $"Handled: {msg.Value}";
        }
    }

    public class NestedChildHandler(ScopedProbe probe)
    {
        public static List<ScopedProbe> Probes { get; } = [];

        public Task HandleAsync(NestedChildMessage msg, CancellationToken ct)
        {
            lock (Probes)
                Probes.Add(probe);
            return Task.CompletedTask;
        }
    }

    [Handler(Lifetime = MediatorLifetime.ScopedPerInvoke)]
    public class NestedIsolatedChildHandler(ScopedProbe probe)
    {
        public static List<ScopedProbe> Probes { get; } = [];

        public Task HandleAsync(NestedIsolatedChildMessage msg, CancellationToken ct)
        {
            lock (Probes)
                Probes.Add(probe);
            return Task.CompletedTask;
        }
    }

    [Handler(Lifetime = MediatorLifetime.ScopedPerInvoke)]
    public class SyncScopedPerInvokeHandler(ScopedProbe probe)
    {
        public static List<ScopedProbe> Probes { get; } = [];

        public string Handle(SyncScopedPerInvokeMessage msg)
        {
            lock (Probes)
                Probes.Add(probe);
            return $"Handled: {msg.Value}";
        }
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddScoped<ScopedProbe>();
        services.AddMediator(b => b.AddAssembly<ScopedPerInvokeMessage>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ScopedPerInvoke_FreshScopePerInvocation()
    {
        ScopedPerInvokeHandler.Probes.Clear();

        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.InvokeAsync<string>(new ScopedPerInvokeMessage("request 1"), TestContext.Current.CancellationToken);
        await mediator.InvokeAsync<string>(new ScopedPerInvokeMessage("request 2"), TestContext.Current.CancellationToken);
        await mediator.InvokeAsync<string>(new ScopedPerInvokeMessage("request 3"), TestContext.Current.CancellationToken);

        Assert.Equal(3, ScopedPerInvokeHandler.Probes.Count);
        Assert.Equal(3, ScopedPerInvokeHandler.Probes.Select(p => p.InstanceId).Distinct().Count());
    }

    [Fact]
    public async Task ScopedPerInvoke_ScopeIsDisposedAfterInvocation()
    {
        ScopedPerInvokeHandler.Probes.Clear();

        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.InvokeAsync<string>(new ScopedPerInvokeMessage("request"), TestContext.Current.CancellationToken);

        var probe = Assert.Single(ScopedPerInvokeHandler.Probes);
        Assert.True(probe.IsDisposed, "The invocation's DI scope should be disposed when the invocation completes");
    }

    [Fact]
    public async Task AmbientHandler_SharesCallerScopeAcrossInvocations()
    {
        AmbientHandler.Probes.Clear();

        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.InvokeAsync<string>(new AmbientMessage("request 1"), TestContext.Current.CancellationToken);
        await mediator.InvokeAsync<string>(new AmbientMessage("request 2"), TestContext.Current.CancellationToken);

        // Ambient (Scoped) handlers resolve from the caller's scope — same instance both times
        Assert.Equal(2, AmbientHandler.Probes.Count);
        Assert.Single(AmbientHandler.Probes.Select(p => p.InstanceId).Distinct());
    }

    [Fact]
    public async Task CascadingMessages_RunInOriginatingHandlersScope()
    {
        CascadeSourceHandler.Probes.Clear();
        CascadedEventHandler.Probes.Clear();

        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.InvokeAsync<string>(new CascadeSourceMessage("order"), TestContext.Current.CancellationToken);

        var sourceProbe = Assert.Single(CascadeSourceHandler.Probes);
        var cascadeProbe = Assert.Single(CascadedEventHandler.Probes);

        // The cascade target is ambient, so its ambient scope is the originating handler's
        // per-invocation scope. Resolving the same probe instance proves the scope was still
        // alive when the cascade dispatched.
        Assert.Equal(sourceProbe.InstanceId, cascadeProbe.InstanceId);
    }

    [Fact]
    public async Task NestedDispatch_AmbientChildSharesParentScope_IsolatedChildGetsOwnScope()
    {
        NestedParentHandler.Probes.Clear();
        NestedChildHandler.Probes.Clear();
        NestedIsolatedChildHandler.Probes.Clear();

        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.InvokeAsync<string>(new NestedParentMessage("parent"), TestContext.Current.CancellationToken);

        var parentProbe = Assert.Single(NestedParentHandler.Probes);
        var childProbe = Assert.Single(NestedChildHandler.Probes);
        var isolatedProbe = Assert.Single(NestedIsolatedChildHandler.Probes);

        // Ambient nested dispatch shares the parent's per-invocation scope
        Assert.Equal(parentProbe.InstanceId, childProbe.InstanceId);
        // ScopedPerInvoke nested dispatch opens its own scope
        Assert.NotEqual(parentProbe.InstanceId, isolatedProbe.InstanceId);
        // The nested isolated scope is disposed when the nested invocation completes
        Assert.True(isolatedProbe.IsDisposed);
    }

    [Fact]
    public async Task ScopedPerInvoke_SyncInvoke_FreshScopePerInvocationAndDisposed()
    {
        SyncScopedPerInvokeHandler.Probes.Clear();

        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        mediator.Invoke<string>(new SyncScopedPerInvokeMessage("request 1"), TestContext.Current.CancellationToken);
        mediator.Invoke<string>(new SyncScopedPerInvokeMessage("request 2"), TestContext.Current.CancellationToken);

        Assert.Equal(2, SyncScopedPerInvokeHandler.Probes.Count);
        Assert.Equal(2, SyncScopedPerInvokeHandler.Probes.Select(p => p.InstanceId).Distinct().Count());
        Assert.All(SyncScopedPerInvokeHandler.Probes, p => Assert.True(p.IsDisposed));
    }
}
