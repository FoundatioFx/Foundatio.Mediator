using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Foundatio.Mediator.Distributed.Tests;

public record PublisherOnlyEvent(int Number) : IDistributedNotification;

public class NotificationReliabilityTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReceiveNotificationsDisabled_StartsAndPublishesWithoutSubscribing()
    {
        var bus = new ControlledBus();
        await using var provider = CreateProvider(bus, configure: o => o.ReceiveNotifications = false);
        var hosted = await provider.StartHostedServicesAsync(CT);
        try
        {
            Assert.Equal(0, bus.SubscriptionCount);
            await provider.GetRequiredService<IMediator>().PublishAsync(new PublisherOnlyEvent(1), CT);
            Assert.Equal(1, await bus.Published.Reader.ReadAsync(CT).AsTask().WaitAsync(TimeSpan.FromSeconds(5), CT));
            Assert.Equal(0, bus.SubscriptionCount);
        }
        finally { await hosted.StopAllAsync(); }
    }

    [Fact]
    public async Task PublisherOnly_StartEstablishesSubscriptionBeforeFirstPublish()
    {
        var bus = new ControlledBus();
        await using var provider = CreateProvider(bus);
        Assert.Empty(provider.GetRequiredService<HandlerRegistry>().Registrations);
        var hosted = await provider.StartHostedServicesAsync(CT);
        try
        {
            await provider.GetRequiredService<IMediator>().PublishAsync(new PublisherOnlyEvent(1), CT);
            Assert.Equal(1, await bus.Published.Reader.ReadAsync(CT).AsTask().WaitAsync(TimeSpan.FromSeconds(5), CT));
        }
        finally { await hosted.StopAllAsync(); }
    }

    [Fact]
    public async Task FullBuffer_DropsOldestExactlyOnce_AndFiltersBeforeBuffering()
    {
        var bus = new ControlledBus { HoldFirst = true };
        await using var provider = CreateProvider(bus);
        var hosted = await provider.StartHostedServicesAsync(CT);
        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            var worker = Assert.Single(hosted.OfType<DistributedNotificationWorker>());
            await mediator.PublishAsync(new PublisherOnlyEvent(1), CT);
            await bus.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
            await mediator.PublishAsync(new PublisherOnlyEvent(2), CT);
            await mediator.PublishAsync(new PublisherOnlyEvent(3), CT);
            for (int i = 0; i < 20; i++)
                await mediator.PublishAsync((object)$"local-{i}", CT);
            Assert.Equal(0, worker.DroppedCount);
            await mediator.PublishAsync(new PublisherOnlyEvent(4), CT);
            Assert.Equal(1, worker.DroppedCount);
            bus.Release.TrySetResult();
            foreach (int expected in new[] { 1, 3, 4 })
                Assert.Equal(expected, await bus.Published.Reader.ReadAsync(CT).AsTask().WaitAsync(TimeSpan.FromSeconds(5), CT));
            Assert.False(bus.Published.Reader.TryRead(out _));
        }
        finally
        {
            bus.Release.TrySetResult();
            await hosted.StopAllAsync();
        }
    }

    [Fact]
    public async Task InMemorySubscriberFailure_IsObservableAndSubscriberContinues()
    {
        await using var bus = new InMemoryPubSubClient();
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bus.DeliveryFailed += exception => error.TrySetResult(exception);
        int calls = 0;
        await using var subscription = await bus.SubscribeAsync("topic", (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("subscriber failure");
            delivered.TrySetResult();
            return Task.CompletedTask;
        }, CT);
        await bus.PublishAsync("topic", [new PubSubEntry { Body = "{}"u8.ToArray() }, new PubSubEntry { Body = "{}"u8.ToArray() }], CT);
        Assert.Equal("subscriber failure", (await error.Task.WaitAsync(TimeSpan.FromSeconds(5), CT)).Message);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task PublisherOnlyNode_DeliversToAnotherNodeImmediatelyAfterStartup()
    {
        await using var bus = new InMemoryPubSubClient();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sender = CreateProvider(bus, configure: o => o.ReceiveNotifications = false);
        await using var receiver = CreateProvider(bus, onReceived: _ => received.TrySetResult());
        var hostedSender = await sender.StartHostedServicesAsync(CT);
        var hostedReceiver = await receiver.StartHostedServicesAsync(CT);
        try
        {
            await sender.GetRequiredService<IMediator>().PublishAsync(new PublisherOnlyEvent(1), CT);
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
            Assert.Empty(sender.GetRequiredService<HandlerRegistry>().Registrations);
        }
        finally
        {
            await hostedSender.StopAllAsync();
            await hostedReceiver.StopAllAsync();
        }
    }

    [Fact]
    public async Task InboundWhileOutboundStalled_DoesNotOverflowOrRebroadcast()
    {
        var bus = new ControlledBus { HoldFirst = true };
        int received = 0;
        await using var provider = CreateProvider(bus, onReceived: _ => received++);
        var hosted = await provider.StartHostedServicesAsync(CT);
        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            await mediator.PublishAsync(new PublisherOnlyEvent(1), CT);
            await bus.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
            for (int i = 0; i < 10_002; i++)
                await bus.InjectAsync(new PublisherOnlyEvent(i + 10), CT);
            Assert.Equal(10_003, received);
            var worker = Assert.Single(hosted.OfType<DistributedNotificationWorker>());
            Assert.Equal(0, worker.DroppedCount);
            await mediator.PublishAsync(new PublisherOnlyEvent(-1), CT);
            bus.Release.TrySetResult();
            Assert.Equal(1, await bus.Published.Reader.ReadAsync(CT));
            Assert.Equal(-1, await bus.Published.Reader.ReadAsync(CT));
            Assert.False(bus.Published.Reader.TryRead(out _));
        }
        finally
        {
            bus.Release.TrySetResult();
            await hosted.StopAllAsync();
        }
    }

    [Fact]
    public async Task ExcludedInboundType_RemainsExcludedWhenRegisteredInResolver()
    {
        var bus = new ControlledBus();
        int received = 0;
        await using var provider = CreateProvider(bus, onReceived: _ => received++, configure: options => options.Exclude<PublisherOnlyEvent>());
        provider.GetRequiredService<MessageTypeResolver>().Register(typeof(PublisherOnlyEvent));
        var hosted = await provider.StartHostedServicesAsync(CT);
        try
        {
            await bus.InjectAsync(new PublisherOnlyEvent(1), CT);
            Assert.Equal(0, received);
            Assert.False(bus.Published.Reader.TryRead(out _));
        }
        finally { await hosted.StopAllAsync(); }
    }

    [Fact]
    public async Task ConcurrentPublications_RespectLimit_AndStopWhileTransportIsStalled()
    {
        var bus = new ControlledBus { HoldAll = true };
        await using var provider = CreateProvider(bus, configure: o => { o.MaxConcurrentPublishes = 4; o.MaxCapacity = 20; });
        var hosted = await provider.StartHostedServicesAsync(CT);
        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            for (int i = 0; i < 9; i++) await mediator.PublishAsync(new PublisherOnlyEvent(i), CT);
            for (int i = 0; i < 4; i++) await bus.Entered.Reader.ReadAsync(CT).AsTask().WaitAsync(TimeSpan.FromSeconds(5), CT);
            Assert.Equal(4, bus.Active);
            Assert.False(bus.Entered.Reader.TryRead(out _));
            await hosted.StopAllAsync().WaitAsync(TimeSpan.FromSeconds(5), CT);
            Assert.Equal(0, bus.Active);
        }
        finally { bus.Release.TrySetResult(); await hosted.StopAllAsync(); }
    }

    [Fact]
    public async Task ConcurrentPublicationFailure_DoesNotStopOtherPublications()
    {
        var bus = new ControlledBus { FailNumber = 2 };
        await using var provider = CreateProvider(bus, configure: o => { o.MaxConcurrentPublishes = 4; o.MaxCapacity = 20; });
        var hosted = await provider.StartHostedServicesAsync(CT);
        try
        {
            var mediator = provider.GetRequiredService<IMediator>();
            for (int i = 0; i < 10; i++) await mediator.PublishAsync(new PublisherOnlyEvent(i), CT);
            var received = new List<int>();
            for (int i = 0; i < 9; i++) received.Add(await bus.Published.Reader.ReadAsync(CT).AsTask().WaitAsync(TimeSpan.FromSeconds(5), CT));
            Assert.Equal(Enumerable.Range(0, 10).Where(i => i != 2), received.Order());
            await mediator.PublishAsync(new PublisherOnlyEvent(10), CT);
            Assert.Equal(10, await bus.Published.Reader.ReadAsync(CT).AsTask().WaitAsync(TimeSpan.FromSeconds(5), CT));
        }
        finally { await hosted.StopAllAsync(); }
    }

    private static ServiceProvider CreateProvider(IPubSubClient bus, Action<object>? onReceived = null, Action<DistributedNotificationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(bus);
        var builder = services.AddMediator(options => options.AddAssembly<IMediator>());
        if (onReceived is not null)
        {
            var registry = new HandlerRegistry();
            registry.AddHandler(new HandlerRegistration(typeof(PublisherOnlyEvent).FullName!, "test", (_, message, _, _, _, _) =>
            {
                onReceived(message);
                return new ValueTask<object?>();
            }, null, true));
            registry.Freeze();
            services.RemoveAll<HandlerRegistry>();
            services.AddSingleton(registry);
        }
        // These tests assert single-publication ordering and exact capacity independently of concurrency.
        builder.AddDistributedNotifications(options => { options.MaxCapacity = 2; options.MaxConcurrentPublishes = 1; configure?.Invoke(options); });
        return services.BuildServiceProvider();
    }

    private sealed class ControlledBus : IPubSubClient
    {
        public int SubscriptionCount { get; private set; }
        public Channel<int> Published { get; } = Channel.CreateUnbounded<int>();
        public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldFirst { get; init; }
        public bool HoldAll { get; init; }
        public int? FailNumber { get; init; }
        public Channel<int> Entered { get; } = Channel.CreateUnbounded<int>();
        private int _active;
        public int Active => Volatile.Read(ref _active);
        private Func<PubSubMessage, CancellationToken, Task>? _handler;
        public Task InjectAsync(PublisherOnlyEvent message, CancellationToken ct)
            => _handler!(new PubSubMessage
            {
                Body = JsonSerializer.SerializeToUtf8Bytes(message),
                Headers = new Dictionary<string, string> { [MessageHeaders.MessageType] = typeof(PublisherOnlyEvent).FullName!, [MessageHeaders.OriginHostId] = "remote" }
            }, ct);
        public async Task PublishAsync(string topic, IReadOnlyList<PubSubEntry> entries, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _active);
            try
            {
                Entered.Writer.TryWrite(0);
                if (HoldAll || (HoldFirst && !FirstEntered.Task.IsCompleted))
                {
                    FirstEntered.TrySetResult();
                    await Release.Task.WaitAsync(ct);
                }
                foreach (var entry in entries)
                {
                    int number = JsonSerializer.Deserialize<PublisherOnlyEvent>(entry.Body.Span)!.Number;
                    if (number == FailNumber) throw new InvalidOperationException("test publication failure");
                    Published.Writer.TryWrite(number);
                }
            }
            finally { Interlocked.Decrement(ref _active); }
        }

        public Task<IAsyncDisposable> SubscribeAsync(string topic, Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken ct = default)
        {
            SubscriptionCount++;
            _handler = handler;
            return Task.FromResult<IAsyncDisposable>(new EmptySubscription());
        }
        private sealed class EmptySubscription : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
