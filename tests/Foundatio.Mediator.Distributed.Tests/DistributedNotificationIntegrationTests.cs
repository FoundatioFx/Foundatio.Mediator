#pragma warning disable xUnit1051
using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Foundatio.Mediator.Distributed.Tests;

// ── Distributed notification messages ─────────────────────────────────
public record TestDistributedEvent(string Value) : IDistributedNotification;
public record AnotherDistributedEvent(int Number) : IDistributedNotification;
public record NonDistributedEvent(string Value) : INotification;

// ── Handlers ──────────────────────────────────────────────────────────
public class TestDistributedEventHandler(HandlerSignal signal)
{
    public void Handle(TestDistributedEvent message) => signal.Record(message.Value);
}

public class AnotherDistributedEventHandler(HandlerSignal signal)
{
    public void Handle(AnotherDistributedEvent message) => signal.Record(message.Number.ToString());
}

public class NonDistributedEventHandler(HandlerSignal signal)
{
    public void Handle(NonDistributedEvent message) => signal.Record(message.Value);
}

// ── Tests ─────────────────────────────────────────────────────────────
public class DistributedNotificationIntegrationTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    /// <summary>
    /// Single-node: publishing a distributed notification fires local handlers
    /// and the worker publishes to the bus. Since there's only one node with
    /// the same HostId, the inbound side skips the message.
    /// </summary>
    [Fact]
    public async Task PublishAsync_SingleNode_LocalHandlerFires()
    {
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<TestDistributedEventHandler>());
        services.AddMediatorDistributedNotifications();

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            await mediator.PublishAsync(new TestDistributedEvent("hello"), cts.Token);

            // Local handler should fire
            await signal.WaitAsync(timeout: TimeSpan.FromSeconds(5));
            Assert.Single(signal.Values);
            Assert.Equal("hello", signal.Values[0]);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Two-node simulation: Node A publishes, Node B receives from bus and
    /// re-publishes locally → Node B's handler fires.
    /// </summary>
    [Fact]
    public async Task PublishAsync_TwoNodes_RemoteHandlerFires()
    {
        // Shared bus simulating a network transport
        var sharedBus = new InMemoryPubSubClient();

        var signalA = new HandlerSignal();
        var signalB = new HandlerSignal();

        // ── Node A ──
        var servicesA = new ServiceCollection();
        servicesA.AddLogging();
        servicesA.AddSingleton(signalA);
        servicesA.AddSingleton<IPubSubClient>(sharedBus);
        servicesA.AddMediator(b => b.AddAssembly<TestDistributedEventHandler>());
        servicesA.AddMediatorDistributedNotifications(opts => opts.HostId = "node-a");

        // ── Node B ──
        var servicesB = new ServiceCollection();
        servicesB.AddLogging();
        servicesB.AddSingleton(signalB);
        servicesB.AddSingleton<IPubSubClient>(sharedBus);
        servicesB.AddMediator(b => b.AddAssembly<TestDistributedEventHandler>());
        servicesB.AddMediatorDistributedNotifications(opts => opts.HostId = "node-b");

        await using var providerA = servicesA.BuildServiceProvider();
        await using var providerB = servicesB.BuildServiceProvider();

        var hostedA = providerA.GetServices<IHostedService>().ToList();
        var hostedB = providerB.GetServices<IHostedService>().ToList();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        foreach (var svc in hostedA.Concat(hostedB))
            await svc.StartAsync(cts.Token);

        try
        {
            // Give workers a moment to set up subscriptions
            await Task.Delay(200, cts.Token);

            // Node A publishes
            var mediatorA = providerA.GetRequiredService<IMediator>();
            await mediatorA.PublishAsync(new TestDistributedEvent("from-A"), cts.Token);

            // Node A's local handler fires
            await signalA.WaitAsync(timeout: TimeSpan.FromSeconds(5));
            Assert.Single(signalA.Values);
            Assert.Equal("from-A", signalA.Values[0]);

            // Node B should receive from bus and fire its local handler
            await signalB.WaitAsync(timeout: TimeSpan.FromSeconds(5));
            Assert.Single(signalB.Values);
            Assert.Equal("from-A", signalB.Values[0]);
        }
        finally
        {
            foreach (var svc in hostedA.Concat(hostedB))
                await svc.StopAsync(CancellationToken.None);
            sharedBus.Dispose();
        }
    }

    /// <summary>
    /// Verifies that a node does NOT re-broadcast a message it received from the bus —
    /// the reference set check prevents the outbound loop from sending it back.
    /// </summary>
    [Fact]
    public async Task PublishAsync_TwoNodes_NoBroadcastLoop()
    {
        var sharedBus = new InMemoryPubSubClient();

        int busPublishCount = 0;
        var countingBus = new CountingPubSubClient(sharedBus, () => Interlocked.Increment(ref busPublishCount));

        var signalA = new HandlerSignal();
        var signalB = new HandlerSignal();

        // ── Node A (uses counting bus to track publishes) ──
        var servicesA = new ServiceCollection();
        servicesA.AddLogging();
        servicesA.AddSingleton(signalA);
        servicesA.AddSingleton<IPubSubClient>(countingBus);
        servicesA.AddMediator(b => b.AddAssembly<TestDistributedEventHandler>());
        servicesA.AddMediatorDistributedNotifications(opts => opts.HostId = "node-a");

        // ── Node B (also uses counting bus) ──
        var servicesB = new ServiceCollection();
        servicesB.AddLogging();
        servicesB.AddSingleton(signalB);
        servicesB.AddSingleton<IPubSubClient>(countingBus);
        servicesB.AddMediator(b => b.AddAssembly<TestDistributedEventHandler>());
        servicesB.AddMediatorDistributedNotifications(opts => opts.HostId = "node-b");

        await using var providerA = servicesA.BuildServiceProvider();
        await using var providerB = servicesB.BuildServiceProvider();

        var hostedA = providerA.GetServices<IHostedService>().ToList();
        var hostedB = providerB.GetServices<IHostedService>().ToList();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        foreach (var svc in hostedA.Concat(hostedB))
            await svc.StartAsync(cts.Token);

        try
        {
            await Task.Delay(200, cts.Token);

            var mediatorA = providerA.GetRequiredService<IMediator>();
            await mediatorA.PublishAsync(new TestDistributedEvent("once"), cts.Token);

            // Wait for both handlers to fire
            await signalA.WaitAsync(timeout: TimeSpan.FromSeconds(5));
            await signalB.WaitAsync(timeout: TimeSpan.FromSeconds(5));

            // Wait a bit more to allow any potential re-broadcast to happen
            await Task.Delay(500, cts.Token);

            // There should be exactly 1 bus publish (from Node A's outbound)
            // Node B should NOT re-publish because the reference set prevents it
            Assert.Equal(1, busPublishCount);

            // Each handler should have been called exactly once
            Assert.Single(signalA.Values);
            Assert.Single(signalB.Values);
        }
        finally
        {
            foreach (var svc in hostedA.Concat(hostedB))
                await svc.StopAsync(CancellationToken.None);
            sharedBus.Dispose();
        }
    }

    /// <summary>
    /// Non-distributed notifications should NOT be published to the bus.
    /// </summary>
    [Fact]
    public async Task PublishAsync_NonDistributed_NotSentToBus()
    {
        int busPublishCount = 0;
        var sharedBus = new InMemoryPubSubClient();
        var countingBus = new CountingPubSubClient(sharedBus, () => Interlocked.Increment(ref busPublishCount));

        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddSingleton<IPubSubClient>(countingBus);
        services.AddMediator(b => b.AddAssembly<NonDistributedEventHandler>());
        services.AddMediatorDistributedNotifications();

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            await mediator.PublishAsync(new NonDistributedEvent("local-only"), cts.Token);

            // Local handler fires
            await signal.WaitAsync(timeout: TimeSpan.FromSeconds(5));
            Assert.Single(signal.Values);

            // Give time for any bus activity
            await Task.Delay(500, cts.Token);

            // Bus should have zero publishes — NonDistributedEvent doesn't implement IDistributedNotification
            Assert.Equal(0, busPublishCount);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
            sharedBus.Dispose();
        }
    }

    /// <summary>
    /// Self-delivery prevention: messages with matching HostId are skipped
    /// by the inbound loop. This test uses a single node — the bus message
    /// published by outbound arrives back at the same node and should be ignored.
    /// </summary>
    [Fact]
    public async Task InboundLoop_SkipsSelfDelivery()
    {
        var signal = new HandlerSignal();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(signal);
        services.AddMediator(b => b.AddAssembly<TestDistributedEventHandler>());
        services.AddMediatorDistributedNotifications(opts => opts.HostId = "self");

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var svc in hostedServices)
            await svc.StartAsync(cts.Token);

        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            await mediator.PublishAsync(new TestDistributedEvent("self-test"), cts.Token);

            // Local handler fires once (from the initial local publish)
            await signal.WaitAsync(timeout: TimeSpan.FromSeconds(5));

            // Wait to ensure no double-fire from bus loopback
            await Task.Delay(500, cts.Token);

            Assert.Single(signal.Values);
            Assert.Equal("self-test", signal.Values[0]);
        }
        finally
        {
            foreach (var svc in hostedServices)
                await svc.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Multiple different notification types all fan out correctly.
    /// </summary>
    [Fact]
    public async Task PublishAsync_MultipleDifferentTypes_AllFanOut()
    {
        var sharedBus = new InMemoryPubSubClient();

        var signalA = new HandlerSignal();
        var signalB = new HandlerSignal();

        var servicesA = new ServiceCollection();
        servicesA.AddLogging();
        servicesA.AddSingleton(signalA);
        servicesA.AddSingleton<IPubSubClient>(sharedBus);
        servicesA.AddMediator(b => b.AddAssembly<TestDistributedEventHandler>());
        servicesA.AddMediatorDistributedNotifications(opts => opts.HostId = "node-a");

        var servicesB = new ServiceCollection();
        servicesB.AddLogging();
        servicesB.AddSingleton(signalB);
        servicesB.AddSingleton<IPubSubClient>(sharedBus);
        servicesB.AddMediator(b => b.AddAssembly<TestDistributedEventHandler>());
        servicesB.AddMediatorDistributedNotifications(opts => opts.HostId = "node-b");

        await using var providerA = servicesA.BuildServiceProvider();
        await using var providerB = servicesB.BuildServiceProvider();

        var hostedA = providerA.GetServices<IHostedService>().ToList();
        var hostedB = providerB.GetServices<IHostedService>().ToList();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        foreach (var svc in hostedA.Concat(hostedB))
            await svc.StartAsync(cts.Token);

        try
        {
            await Task.Delay(200, cts.Token);

            var mediatorA = providerA.GetRequiredService<IMediator>();
            await mediatorA.PublishAsync(new TestDistributedEvent("event1"), cts.Token);
            await mediatorA.PublishAsync(new AnotherDistributedEvent(42), cts.Token);

            // Node A fires both handler types locally
            await signalA.WaitAsync(count: 2, timeout: TimeSpan.FromSeconds(5));

            // Node B receives both from bus
            await signalB.WaitAsync(count: 2, timeout: TimeSpan.FromSeconds(5));

            Assert.Contains("event1", signalB.Values);
            Assert.Contains("42", signalB.Values);
        }
        finally
        {
            foreach (var svc in hostedA.Concat(hostedB))
                await svc.StopAsync(CancellationToken.None);
            sharedBus.Dispose();
        }
    }
}

// ── Test helper: counting bus decorator ──────────────────────────────
internal sealed class CountingPubSubClient(IPubSubClient inner, Action onPublish) : IPubSubClient
{
    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> body, IDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        onPublish();
        await inner.PublishAsync(topic, body, headers, cancellationToken);
    }

    public Task<IAsyncDisposable> SubscribeAsync(string topic, Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
        => inner.SubscribeAsync(topic, handler, cancellationToken);
}
