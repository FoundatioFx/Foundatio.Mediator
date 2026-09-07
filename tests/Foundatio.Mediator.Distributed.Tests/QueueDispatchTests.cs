using System.Text;
using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Foundatio.Mediator.Distributed.Tests;

// ── Messages ─────────────────────────────────────────────────────────

public record SharedQueueEvent(string Value);

public interface ITriggerEvent
{
    string Value { get; }
}

public record TriggerAlpha(string Value) : ITriggerEvent;
public record TriggerBeta(string Value) : ITriggerEvent;

public record QueuedDistributedEvent(string Value) : IDistributedNotification;

public interface IScriptTrigger : IDistributedNotification
{
    string Script { get; }
}

public record ObligationPaid(string Script) : IScriptTrigger;

public class ScriptTriggerHandler(HandlerSignal signal)
{
    public void Handle(IScriptTrigger trigger) => signal.Record($"{trigger.GetType().Name}:{trigger.Script}");
}

public record PoisonBodyMessage(string Value);

public record TenantScopedCommand(string Value);

public record MetadataTrackedCommand(string Value, string Tenant);

public sealed class TenantContext(string tenant)
{
    public string Tenant { get; } = tenant;
}

// ── Handlers ─────────────────────────────────────────────────────────

[Queue(QueueName = "shared-queue-events", DisplayName = "Shared events")]
public class SharedQueueAuditHandler(HandlerSignal signal)
{
    public void Handle(SharedQueueEvent message) => signal.Record($"audit:{message.Value}");
}

[Queue(QueueName = "shared-queue-events")]
public class SharedQueueNotifyHandler(HandlerSignal signal)
{
    public void Handle(SharedQueueEvent message) => signal.Record($"notify:{message.Value}");
}

[Queue(QueueName = nameof(ITriggerEvent))]
public class TriggerEventHandler(HandlerSignal signal)
{
    public void Handle(ITriggerEvent message, QueueContext queueContext)
        => signal.Record($"{message.GetType().Name}:{message.Value}:{queueContext.QueueName}");
}

[Queue]
public class QueuedDistributedEventHandler(HandlerSignal signal)
{
    public void Handle(QueuedDistributedEvent message) => signal.Record(message.Value);
}

[Queue(MaxAttempts = 3)]
public class PoisonBodyMessageHandler(HandlerSignal signal)
{
    public void Handle(PoisonBodyMessage message) => signal.Record(message.Value);
}

[Queue]
public class TenantScopedCommandHandler(HandlerSignal signal)
{
    public void Handle(TenantScopedCommand message, TenantContext tenant) => signal.Record($"{tenant.Tenant}:{message.Value}");
}

[Queue(DisplayName = "Tracked work", TrackProgress = true)]
public class MetadataTrackedCommandHandler(HandlerSignal signal)
{
    public Result Handle(MetadataTrackedCommand message)
    {
        signal.Record(message.Value);
        return Result.Ok();
    }
}

public record LockedUpload(string Bank) : IHaveLockKey
{
    public string GetLockKey() => $"upload:{Bank}";
}

[Queue]
[QueueLock]
public class LockedUploadHandler(HandlerSignal signal)
{
    public void Handle(LockedUpload message) => signal.Record(message.Bank);
}

public sealed class TenantHeaderProvider : IQueueHeaderProvider
{
    public void Enrich(object message, IDictionary<string, string> headers)
    {
        if (message is TenantScopedCommand)
            headers["x-tenant"] = "acme";
    }

    public void Restore(IReadOnlyDictionary<string, string> headers, CallContext callContext)
    {
        if (headers.TryGetValue("x-tenant", out var tenant))
            callContext.Set(new TenantContext(tenant));
    }
}

// ── Test helpers ─────────────────────────────────────────────────────

internal sealed class CountingQueueClient(IQueueClient inner) : IQueueClient
{
    public bool IsDistributed => inner.IsDistributed;
    private int _sends;
    public int Sends => _sends;

    public Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default)
    {
        Interlocked.Add(ref _sends, entries.Count);
        return inner.SendAsync(queueName, entries, cancellationToken);
    }

    public Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, TimeSpan? visibilityTimeout, CancellationToken cancellationToken = default)
        => inner.ReceiveAsync(queueName, maxCount, visibilityTimeout, cancellationToken);

    public Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default) => inner.CompleteAsync(message, cancellationToken);
    public Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default) => inner.AbandonAsync(message, delay, cancellationToken);
    public Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default) => inner.RenewTimeoutAsync(message, extension, cancellationToken);
    public Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default) => inner.DeadLetterAsync(message, reason, cancellationToken);
}

internal static class HostedServiceExtensions
{
    public static async Task<List<IHostedService>> StartHostedServicesAsync(this IServiceProvider provider, CancellationToken ct)
    {
        var services = provider.GetServices<IHostedService>().ToList();
        foreach (var lifecycle in services.OfType<IHostedLifecycleService>())
            await lifecycle.StartingAsync(ct);
        foreach (var svc in services)
            await svc.StartAsync(ct);
        foreach (var lifecycle in services.OfType<IHostedLifecycleService>())
            await lifecycle.StartedAsync(ct);
        return services;
    }

    public static async Task StopAllAsync(this IEnumerable<IHostedService> services)
    {
        foreach (var svc in services)
            await svc.StopAsync(CancellationToken.None);
    }
}

// ── Tests ────────────────────────────────────────────────────────────

public class QueueDispatchTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    [Fact]
    public async Task SharedQueue_PublishOnce_EnqueuesOneMessage_EveryHandlerRuns()
    {
        var signal = new HandlerSignal();
        var queueClient = new CountingQueueClient(new InMemoryQueueClient());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddSingleton<IQueueClient>(queueClient);
        services.AddMediator(b => b.AddAssembly<SharedQueueAuditHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        var topology = provider.GetRequiredService<QueueTopology>();
        var shared = topology.GetByQueueName("shared-queue-events");
        Assert.NotNull(shared);
        Assert.Equal(2, shared.Handlers.Count);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var hosted = await provider.StartHostedServicesAsync(cts.Token);
        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            await mediator.PublishAsync(new SharedQueueEvent("order-1"), cts.Token);

            await signal.WaitAsync(count: 2, timeout: TimeSpan.FromSeconds(10));
            await Task.Delay(300, cts.Token);

            Assert.Equal(1, queueClient.Sends);
            Assert.Equal(2, signal.Values.Count);
            Assert.Contains("audit:order-1", signal.Values);
            Assert.Contains("notify:order-1", signal.Values);
        }
        finally
        {
            await hosted.StopAllAsync();
        }
    }

    [Fact]
    public async Task InterfaceTypedHandler_ReceivesConcreteTypes()
    {
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<TriggerEventHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var hosted = await provider.StartHostedServicesAsync(cts.Token);
        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            await mediator.PublishAsync(new TriggerAlpha("a"), cts.Token);
            await mediator.PublishAsync(new TriggerBeta("b"), cts.Token);

            await signal.WaitAsync(count: 2, timeout: TimeSpan.FromSeconds(10));

            Assert.Contains("TriggerAlpha:a:ITriggerEvent", signal.Values);
            Assert.Contains("TriggerBeta:b:ITriggerEvent", signal.Values);
        }
        finally
        {
            await hosted.StopAllAsync();
        }
    }

    [Fact]
    public async Task InboundNotification_QueuedHandler_RunsOnceAcrossTwoNodes()
    {
        var signal = new HandlerSignal();
        var sharedQueue = new CountingQueueClient(new InMemoryQueueClient());
        var sharedBus = new InMemoryPubSubClient();

        ServiceProvider BuildNode(string hostId)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(signal);
            services.AddSingleton<IQueueClient>(sharedQueue);
            services.AddSingleton<IPubSubClient>(sharedBus);
            services.AddMediator(b => b.AddAssembly<QueuedDistributedEventHandler>())
                .AddDistributedQueues()
                .AddDistributedNotifications(o => o.HostId = hostId);
            return services.BuildServiceProvider();
        }

        await using var nodeA = BuildNode("node-a");
        await using var nodeB = BuildNode("node-b");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var hosted = await nodeA.StartHostedServicesAsync(cts.Token);
        hosted.AddRange(await nodeB.StartHostedServicesAsync(cts.Token));
        try
        {
            await Task.Delay(200, cts.Token);

            await nodeA.GetRequiredService<IMediator>().PublishAsync(new QueuedDistributedEvent("once"), cts.Token);

            await signal.WaitAsync(count: 1, timeout: TimeSpan.FromSeconds(10));
            await Task.Delay(750, cts.Token);

            Assert.Single(signal.Values);
            Assert.Equal(1, sharedQueue.Sends);
        }
        finally
        {
            await hosted.StopAllAsync();
            sharedBus.Dispose();
        }
    }

    [Fact]
    public async Task InterfaceTypedNotificationHandler_ReceivesConcreteEventsFromAnotherNode()
    {
        var signalA = new HandlerSignal();
        var signalB = new HandlerSignal();
        var sharedBus = new InMemoryPubSubClient();

        ServiceProvider BuildNode(string hostId, HandlerSignal signal)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(signal);
            services.AddSingleton<IPubSubClient>(sharedBus);
            services.AddMediator(b => b.AddAssembly<ScriptTriggerHandler>())
                .AddDistributedNotifications(o => o.HostId = hostId);
            return services.BuildServiceProvider();
        }

        await using var nodeA = BuildNode("node-a", signalA);
        await using var nodeB = BuildNode("node-b", signalB);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var hosted = await nodeA.StartHostedServicesAsync(cts.Token);
        hosted.AddRange(await nodeB.StartHostedServicesAsync(cts.Token));
        try
        {
            await Task.Delay(200, cts.Token);

            // Only the interface is registered as a handler type; the concrete event is resolved on the receiving node
            await nodeA.GetRequiredService<IMediator>().PublishAsync(new ObligationPaid("late-fee"), cts.Token);

            await signalA.WaitAsync(timeout: TimeSpan.FromSeconds(10));
            await signalB.WaitAsync(timeout: TimeSpan.FromSeconds(10));
            Assert.Equal("ObligationPaid:late-fee", signalB.Values[0]);
        }
        finally
        {
            await hosted.StopAllAsync();
            sharedBus.Dispose();
        }
    }

    [Fact]
    public async Task MalformedBody_IsDeadLetteredOnFirstReceive()
    {
        var signal = new HandlerSignal();
        var queueClient = new InMemoryQueueClient();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddSingleton<IQueueClient>(queueClient);
        services.AddMediator(b => b.AddAssembly<PoisonBodyMessageHandler>())
            .AddDistributedQueues();

        await using var provider = services.BuildServiceProvider();

        await queueClient.SendAsync("PoisonBodyMessage", [new QueueEntry
        {
            Body = Encoding.UTF8.GetBytes("{ this is not json"),
            Headers = new Dictionary<string, string> { [MessageHeaders.MessageType] = typeof(PoisonBodyMessage).FullName! }
        }], TestCancellationToken);

        await queueClient.SendAsync("PoisonBodyMessage", [new QueueEntry
        {
            Body = "{}"u8.ToArray(),
            Headers = new Dictionary<string, string> { [MessageHeaders.MessageType] = "No.Such.Type" }
        }], TestCancellationToken);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var hosted = await provider.StartHostedServicesAsync(cts.Token);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (queueClient.GetDeadLetterCount("PoisonBodyMessage") < 2 && DateTime.UtcNow < deadline)
                await Task.Delay(50, cts.Token);

            var deadLetters = queueClient.DrainDeadLetterMessages("PoisonBodyMessage");
            Assert.Equal(2, deadLetters.Count);
            Assert.Contains(deadLetters, m => m.Headers[MessageHeaders.DeadLetterReason].Contains("Deserialization failed", StringComparison.Ordinal));
            Assert.Contains(deadLetters, m => m.Headers[MessageHeaders.DeadLetterReason].Contains("Unknown message type", StringComparison.Ordinal));
            Assert.All(deadLetters, m => Assert.Equal("1", m.Headers[MessageHeaders.DeadLetterDequeueCount]));
            Assert.Empty(signal.Values);
        }
        finally
        {
            await hosted.StopAllAsync();
        }
    }

    [Fact]
    public async Task HeaderProvider_EnrichesOnEnqueue_RestoresOnWorker()
    {
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<TenantScopedCommandHandler>())
            .AddDistributedQueues()
            .AddQueueHeaderProvider<TenantHeaderProvider>();

        await using var provider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var hosted = await provider.StartHostedServicesAsync(cts.Token);
        try
        {
            await provider.GetRequiredService<IMediator>().InvokeAsync(new TenantScopedCommand("cmd"), cts.Token);
            await signal.WaitAsync(timeout: TimeSpan.FromSeconds(10));
            Assert.Equal("acme:cmd", signal.Values[0]);
        }
        finally
        {
            await hosted.StopAllAsync();
        }
    }

    [Fact]
    public async Task JobMetadataProvider_CapturesMetadataAtEnqueue()
    {
        var queueClient = new InMemoryQueueClient();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new HandlerSignal());
        services.AddSingleton<IQueueClient>(queueClient);
        services.AddMediator(b => b.AddAssembly<MetadataTrackedCommandHandler>())
            .AddDistributedQueues(o => o.JobMetadataProvider = m => m is MetadataTrackedCommand c
                ? new Dictionary<string, string> { ["tenant"] = c.Tenant }
                : null);

        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<IMediator>().EnqueueAsync(new MetadataTrackedCommand("job", "acme"), TestCancellationToken);

        Assert.Equal(ResultStatus.Accepted, result.Status);
        Assert.False(string.IsNullOrEmpty(result.Value.JobId));

        var state = await provider.GetRequiredService<IQueueJobStateStore>().GetJobStateAsync(result.Value.JobId!, TestCancellationToken);
        Assert.NotNull(state);
        Assert.NotNull(state.Metadata);
        Assert.Equal("acme", state.Metadata["tenant"]);

        var messages = await queueClient.ReceiveAsync("MetadataTrackedCommand", 1, TestCancellationToken);
        Assert.Equal(result.Value.JobId, messages[0].Headers[MessageHeaders.JobId]);
        Assert.True(messages[0].Headers.ContainsKey(MessageHeaders.CorrelationId));
    }

    [Fact]
    public async Task QueueLock_HeldElsewhere_PreservesBothMessagesUntilFree()
    {
        var signal = new HandlerSignal();
        var queueClient = new InMemoryQueueClient();
        var lockProvider = new InMemoryQueueLockProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddSingleton<IQueueClient>(queueClient);
        services.AddSingleton<IQueueLockProvider>(lockProvider);
        services.AddMediator(b => b.AddAssembly<LockedUploadHandler>())
            .AddDistributedQueues(o => o.Workers = WorkerSelection.Only("LockedUpload"));

        await using var provider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var hosted = await provider.StartHostedServicesAsync(cts.Token);
        try
        {
            var mediator = provider.GetRequiredService<IMediator>();

            // Another worker owns the resource; both distinct deliveries must wait and eventually execute.
            var held = await lockProvider.TryAcquireAsync("upload:chase", TimeSpan.FromMinutes(1), TimeSpan.Zero, cts.Token);
            Assert.NotNull(held);

            await mediator.InvokeAsync(new LockedUpload("chase"), cts.Token);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (queueClient.GetInFlightCount("LockedUpload") == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(25, cts.Token);

            Assert.Empty(signal.Values);
            Assert.Equal(1, queueClient.GetInFlightCount("LockedUpload"));
            Assert.Equal(0, queueClient.GetDeadLetterCount("LockedUpload"));

            await mediator.InvokeAsync(new LockedUpload("chase"), cts.Token);
            await held.DisposeAsync();
            await signal.WaitAsync(count: 2, timeout: TimeSpan.FromSeconds(10));
            Assert.Equal(2, signal.Values.Count);
            Assert.All(signal.Values, value => Assert.Equal("chase", value));

            // The lock is released after the handler returns, which is after the signal fires.
            deadline = DateTime.UtcNow.AddSeconds(5);
            while (lockProvider.IsLocked("upload:chase") && DateTime.UtcNow < deadline)
                await Task.Delay(25, cts.Token);
            Assert.False(lockProvider.IsLocked("upload:chase"));
        }
        finally
        {
            await hosted.StopAllAsync();
        }
    }

    [Fact]
    public void EnqueueOnlyProcess_WithoutTransport_FailsWhenClientResolved()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new HandlerSignal());

        services.AddMediator(b => b.AddAssembly<QueuedCommandHandler>())
            .AddDistributedQueues(o => o.Workers = WorkerSelection.None);

        // The guard fires when the queue client is first resolved (host start in an app), not at registration,
        // so a transport registered after AddDistributedQueues() is still honored.
        var ex = Assert.Throws<InvalidOperationException>(() => services.BuildServiceProvider().GetRequiredService<IQueueClient>());
        Assert.Contains("no IQueueClient transport", ex.Message, StringComparison.Ordinal);

        var transportAfter = new ServiceCollection();
        transportAfter.AddLogging();
        transportAfter.AddSingleton(new HandlerSignal());
        transportAfter.AddMediator(b => b.AddAssembly<QueuedCommandHandler>())
            .AddDistributedQueues(o => o.Workers = WorkerSelection.None);
        var explicitClient = new InMemoryQueueClient();
        transportAfter.AddSingleton<IQueueClient>(explicitClient);
        Assert.Same(explicitClient, transportAfter.BuildServiceProvider().GetRequiredService<IQueueClient>());

        var allowed = new ServiceCollection();
        allowed.AddLogging();
        allowed.AddSingleton(new HandlerSignal());
        allowed.AddMediator(b => b.AddAssembly<QueuedCommandHandler>())
            .AddDistributedQueues(o =>
            {
                o.Workers = WorkerSelection.None;
                o.AllowInMemoryWithoutWorkers = true;
            });

        var filtered = new ServiceCollection();
        filtered.AddLogging();
        filtered.AddSingleton(new HandlerSignal());
        filtered.AddSingleton<IQueueClient>(new InMemoryQueueClient());
        filtered.AddMediator(b => b.AddAssembly<QueuedCommandHandler>())
            .AddDistributedQueues(o => o.Workers = WorkerSelection.Only("QueuedCommand"));
    }

    [Fact]
    public void WorkerSelection_ParsesConfigurationText()
    {
        Assert.True(WorkerSelection.Parse(null).IsAll);
        Assert.True(WorkerSelection.Parse(" all ").IsAll);
        Assert.True(WorkerSelection.Parse("none").IsNone);

        var only = WorkerSelection.Parse("exports, imports");
        Assert.True(only.Includes("ProcessDataExport", "exports"));
        Assert.True(only.Includes("imports", null));
        Assert.False(only.Includes("RunScriptForEvent", "scripts"));
        Assert.Equal("exports,imports", only.ToString());

        var except = WorkerSelection.Parse("!imports");
        Assert.True(except.Includes("RunScriptForEvent", "scripts"));
        Assert.False(except.Includes("RunDataImport", "imports"));
        Assert.False(except.IsAll);
        Assert.Equal("!imports", except.ToString());

        var mixed = WorkerSelection.Parse("exports,!ProcessGridExport");
        Assert.True(mixed.Includes("ProcessDataExport", "exports"));
        Assert.False(mixed.Includes("ProcessGridExport", "exports"));

        Assert.Throws<ArgumentException>(() => WorkerSelection.Only());
    }

    [Fact]
    public void WorkerSelection_DecidesWhichWorkersRegister()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new HandlerSignal());
        services.AddSingleton<IQueueClient>(new InMemoryQueueClient());
        services.AddMediator(b => b.AddAssembly<SharedQueueAuditHandler>())
            .AddDistributedQueues(o => o.Workers = WorkerSelection.Only("shared-queue-events", "ITriggerEvent"));

        using var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<QueueTopology>();

        Assert.True(topology.GetByQueueName("shared-queue-events")!.WorkerRunsHere);
        Assert.True(topology.GetByQueueName("ITriggerEvent")!.WorkerRunsHere);
        Assert.False(topology.GetByQueueName("QueuedCommand")!.WorkerRunsHere);

        var registry = provider.GetRequiredService<IQueueWorkerRegistry>();
        Assert.True(registry.GetWorker("QueuedCommand") is { Stats.WorkerRegistered: false });
        Assert.Equal(2, registry.GetWorkers().Count(w => w.Stats.WorkerRegistered));
    }

    [Fact]
    public void Topology_ExposesExplicitSharedGroups()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new HandlerSignal());
        services.AddMediator(b => b.AddAssembly<SharedQueueAuditHandler>())
            .AddDistributedQueues(o => o.ResourcePrefix = "test");

        using var provider = services.BuildServiceProvider();
        var topology = provider.GetRequiredService<QueueTopology>();

        var shared = topology.GetByQueueName("test-shared-queue-events");
        Assert.NotNull(shared);
        Assert.True(shared.WorkerRunsHere);

        Assert.Equal(2, shared.Handlers.Count);
        Assert.Same(shared, topology.GetByDescriptorId(shared.Handlers[1].DescriptorId));

        var trigger = topology.GetByQueueName("test-ITriggerEvent");
        Assert.NotNull(trigger);
        Assert.Single(trigger.HandlersFor(typeof(TriggerAlpha)));
        Assert.Empty(trigger.HandlersFor(typeof(SharedQueueEvent)));
    }
}
