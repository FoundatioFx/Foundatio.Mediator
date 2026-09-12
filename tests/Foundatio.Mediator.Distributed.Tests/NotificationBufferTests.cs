using System.Collections.Concurrent;
using Foundatio;
using Foundatio.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed.Tests;

public class NotificationBufferTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExplicitSubscriptions_LocalTrafficDoesNotEvictBufferedClusterEvents()
    {
        var transport = new PausedNotificationTransport(4);
        using var host = await StartAsync(transport, options => options.Include<BufferedEvent>());
        var mediator = host.Services.GetRequiredService<IMediator>();
        try
        {
            await mediator.PublishAsync(new BufferedEvent(0), CT);
            await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
            for (int i = 1; i < 4; i++) await mediator.PublishAsync(new BufferedEvent(i), CT);
            for (int i = 0; i < 10000; i++) await mediator.PublishAsync(new LocalEvent(i), CT);
            transport.Release.TrySetResult();
            await transport.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
            Assert.Equal(4, transport.Messages.Count);
            Assert.All(transport.Messages, message => Assert.Contains(nameof(BufferedEvent), message.Headers[KnownHeaders.MessageType]));
        }
        finally { transport.Release.TrySetResult(); await host.StopAsync(CT); }
    }

    [Fact]
    public async Task OverlappingSubscriptions_PublishOnceAndShareTheConcurrencyLimit()
    {
        var transport = new PausedNotificationTransport(3);
        using var host = await StartAsync(transport, options => options.Include<MarkedEvent>().IncludeAssignableTo<IFirstEvent>().IncludeAssignableTo<ISecondEvent>().Include<BufferedEvent>());
        var mediator = host.Services.GetRequiredService<IMediator>();
        try
        {
            await mediator.PublishAsync(new MarkedEvent(1), CT);
            await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
            await mediator.PublishAsync(new BufferedEvent(2), CT);
            await mediator.PublishAsync(new AttributedEvent(3), CT);
            transport.Release.TrySetResult();
            await transport.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
            await host.StopAsync(CT);
            Assert.Equal(3, transport.Messages.Count);
            Assert.Equal(1, transport.MaximumConcurrency);
        }
        finally { transport.Release.TrySetResult(); await host.StopAsync(CT); }
    }

    [Fact]
    public async Task DynamicRules_KeepRuntimeFilteringAndExclusions()
    {
        var transport = new PausedNotificationTransport(1);
        transport.Release.TrySetResult();
        using var host = await StartAsync(transport, options =>
        {
            options.MessageFilter = type => type == typeof(BufferedEvent) || type == typeof(LocalEvent);
            options.Exclude<LocalEvent>();
        });
        try
        {
            var mediator = host.Services.GetRequiredService<IMediator>();
            await mediator.PublishAsync(new LocalEvent(1), CT);
            await mediator.PublishAsync(new BufferedEvent(2), CT);
            await transport.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(5), CT);
            await host.StopAsync(CT);
            Assert.Single(transport.Messages);
        }
        finally { await host.StopAsync(CT); }
    }

    private static async Task<IHost> StartAsync(PausedNotificationTransport transport, Action<DistributedNotificationOptions> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddFoundatio().Messaging.UseTransport(transport);
        builder.Services.AddSingleton(new MessagingTopologyOptions(TopologyMode.None));
        builder.Services.AddMediator(options => options.AddAssembly<NotificationBufferTests>()).AddDistributedNotifications(options =>
        {
            options.ReceiveNotifications = false;
            options.MaxCapacity = 4;
            options.MaxConcurrentPublishes = 1;
            configure(options);
        });
        var host = builder.Build();
        await host.StartAsync(CT);
        return host;
    }

    public record BufferedEvent(int Id);
    public record LocalEvent(int Id);
    public interface IFirstEvent;
    public interface ISecondEvent;
    public record MarkedEvent(int Id) : IDistributedNotification, IFirstEvent, ISecondEvent;
    [DistributedNotification]
    public record AttributedEvent(int Id);

    private sealed class PausedNotificationTransport(int expected) : IMessageTransport, ITransportInfo
    {
        public DeliveryGuarantee DeliveryGuarantee => DeliveryGuarantee.AtMostOnce;
        public IReadOnlySet<DestinationRole> SupportedRoles { get; } = new HashSet<DestinationRole> { DestinationRole.Topic };
        public TransportCapabilities GetCapabilities(DestinationAddress destination) => new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<TransportMessage> Messages { get; } = new();
        private int _active;
        public int MaximumConcurrency;

        public async Task<SendResult> SendAsync(DestinationAddress destination, IReadOnlyList<TransportMessage> messages, TransportSendOptions options, CancellationToken ct = default)
        {
            int active = Interlocked.Increment(ref _active);
            int previous;
            do { previous = MaximumConcurrency; }
            while (active > previous && Interlocked.CompareExchange(ref MaximumConcurrency, active, previous) != previous);
            try
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
                foreach (var message in messages) Messages.Enqueue(message);
                if (Messages.Count >= expected) Delivered.TrySetResult();
                return new SendResult { Items = messages.Select(message => new SendItemResult { MessageId = message.MessageId }).ToArray() };
            }
            finally { Interlocked.Decrement(ref _active); }
        }

        public Task CompleteAsync(TransportEntry entry, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AbandonAsync(TransportEntry entry, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
