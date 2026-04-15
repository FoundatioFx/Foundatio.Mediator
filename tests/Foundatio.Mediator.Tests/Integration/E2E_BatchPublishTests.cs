using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable xUnit1051

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_BatchPublishTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    public record BatchEvent(string Name);

    public class EventCollector
    {
        private readonly List<string> _events = new();
        public IReadOnlyCollection<string> Events => _events;
        public void AddEvent(string eventName) => _events.Add(eventName);
        public void Reset() => _events.Clear();
    }

    [Handler(Lifetime = MediatorLifetime.Singleton)]
    public class SingleEventHandler(EventCollector collector)
    {
        public Task HandleAsync(BatchEvent message, CancellationToken ct)
        {
            collector.AddEvent($"single:{message.Name}");
            return Task.CompletedTask;
        }
    }

    [Handler(Lifetime = MediatorLifetime.Singleton)]
    public class BatchEventHandler(EventCollector collector)
    {
        public Task HandleAsync(IReadOnlyList<BatchEvent> events, CancellationToken ct)
        {
            collector.AddEvent($"batch:{events.Count}:{string.Join(",", events.Select(e => e.Name))}");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishBatch_SingleHandler_CalledPerMessage()
    {
        var services = new ServiceCollection();
        services.AddSingleton<EventCollector>();
        services.AddMediator();
        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var collector = provider.GetRequiredService<EventCollector>();

        // Force only single handlers to be invoked by publishing individually
        var events = new[] { new BatchEvent("A"), new BatchEvent("B"), new BatchEvent("C") };
        foreach (var e in events)
            await mediator.PublishAsync(e);

        Assert.Contains("single:A", collector.Events);
        Assert.Contains("single:B", collector.Events);
        Assert.Contains("single:C", collector.Events);
    }

    [Fact]
    public async Task PublishBatch_BatchHandler_CalledWithFullBatch()
    {
        var services = new ServiceCollection();
        services.AddSingleton<EventCollector>();
        services.AddMediator();
        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var collector = provider.GetRequiredService<EventCollector>();

        var events = new[] { new BatchEvent("X"), new BatchEvent("Y"), new BatchEvent("Z") };
        await mediator.PublishAsync<BatchEvent>(events);

        // Batch handler should be called once with 3 messages
        Assert.Contains(collector.Events, e => e.StartsWith("batch:3:"));

        // Single handler should be called for each message
        Assert.Contains("single:X", collector.Events);
        Assert.Contains("single:Y", collector.Events);
        Assert.Contains("single:Z", collector.Events);
    }

    [Fact]
    public async Task PublishSingle_BatchHandler_CalledWithSingleItemList()
    {
        var services = new ServiceCollection();
        services.AddSingleton<EventCollector>();
        services.AddMediator();
        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var collector = provider.GetRequiredService<EventCollector>();

        await mediator.PublishAsync(new BatchEvent("Solo"));

        // Single handler gets it
        Assert.Contains("single:Solo", collector.Events);
        // Batch handler gets it wrapped in a single-item list
        Assert.Contains("batch:1:Solo", collector.Events);
    }

    [Fact]
    public async Task PublishBatch_EmptyBatch_IsNoOp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<EventCollector>();
        services.AddMediator();
        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var collector = provider.GetRequiredService<EventCollector>();

        await mediator.PublishAsync<BatchEvent>(Enumerable.Empty<BatchEvent>());

        Assert.Empty(collector.Events);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
            await Task.Delay(10);
    }

    [Fact]
    public async Task BatchPublish_IReadOnlyListSubscriber_ReceivesFullBatch()
    {
        var services = new ServiceCollection();
        services.AddSingleton<EventCollector>();
        services.AddMediator();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received = new List<IReadOnlyList<BatchEvent>>();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var batch in mediator.SubscribeAsync<IReadOnlyList<BatchEvent>>(cts.Token))
                received.Add(batch);
        });

        await WaitUntilAsync(() => registry.HasSubscribers);

        await mediator.PublishAsync<BatchEvent>(new[] { new BatchEvent("A"), new BatchEvent("B") });

        await WaitUntilAsync(() => received.Count >= 1);
        cts.Cancel();
        await subscriberTask;

        Assert.Single(received);
        Assert.Equal(2, received[0].Count);
        Assert.Equal("A", received[0][0].Name);
        Assert.Equal("B", received[0][1].Name);
    }

    [Fact]
    public async Task BatchPublish_ArraySubscriber_ReceivesFullBatch()
    {
        var services = new ServiceCollection();
        services.AddSingleton<EventCollector>();
        services.AddMediator();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received = new List<BatchEvent[]>();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var batch in mediator.SubscribeAsync<BatchEvent[]>(cts.Token))
                received.Add(batch);
        });

        await WaitUntilAsync(() => registry.HasSubscribers);

        await mediator.PublishAsync<BatchEvent>(new[] { new BatchEvent("X"), new BatchEvent("Y") });

        await WaitUntilAsync(() => received.Count >= 1);
        cts.Cancel();
        await subscriberTask;

        Assert.Single(received);
        Assert.Equal(2, received[0].Length);
        Assert.Equal("X", received[0][0].Name);
        Assert.Equal("Y", received[0][1].Name);
    }

    [Fact]
    public async Task SinglePublish_IReadOnlyListSubscriber_ReceivesSingleItemBatch()
    {
        var services = new ServiceCollection();
        services.AddSingleton<EventCollector>();
        services.AddMediator();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var received = new List<IReadOnlyList<BatchEvent>>();

        var subscriberTask = Task.Run(async () =>
        {
            await foreach (var batch in mediator.SubscribeAsync<IReadOnlyList<BatchEvent>>(cts.Token))
                received.Add(batch);
        });

        await WaitUntilAsync(() => registry.HasSubscribers);

        await mediator.PublishAsync(new BatchEvent("Solo"));

        await WaitUntilAsync(() => received.Count >= 1);
        cts.Cancel();
        await subscriberTask;

        Assert.Single(received);
        Assert.Single(received[0]);
        Assert.Equal("Solo", received[0][0].Name);
    }

    [Fact]
    public async Task BatchPublish_IndividualAndBatchSubscribers_BothReceive()
    {
        var services = new ServiceCollection();
        services.AddSingleton<EventCollector>();
        services.AddMediator();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var individualReceived = new List<BatchEvent>();
        var batchReceived = new List<IReadOnlyList<BatchEvent>>();

        var individualTask = Task.Run(async () =>
        {
            await foreach (var item in mediator.SubscribeAsync<BatchEvent>(cts.Token))
                individualReceived.Add(item);
        });

        var batchTask = Task.Run(async () =>
        {
            await foreach (var batch in mediator.SubscribeAsync<IReadOnlyList<BatchEvent>>(cts.Token))
                batchReceived.Add(batch);
        });

        await WaitUntilAsync(() => registry.HasSubscribers);

        await mediator.PublishAsync<BatchEvent>(new[] { new BatchEvent("A"), new BatchEvent("B") });

        await WaitUntilAsync(() => individualReceived.Count >= 2 && batchReceived.Count >= 1);
        cts.Cancel();
        await Task.WhenAll(individualTask, batchTask);

        // Individual subscriber gets each message
        Assert.Equal(2, individualReceived.Count);
        // Batch subscriber gets the full batch once
        Assert.Single(batchReceived);
        Assert.Equal(2, batchReceived[0].Count);
    }
}
