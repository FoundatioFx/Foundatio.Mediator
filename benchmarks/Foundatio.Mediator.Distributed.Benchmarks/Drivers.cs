using System.Diagnostics.Metrics;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Foundatio.Mediator.Distributed.Aws;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributedBenchmarks;

internal sealed class Driver : IAsyncDisposable
{
    private readonly Settings _settings;
    private readonly IHost _host;
    private readonly IMediator? _mediator;
    private readonly IBus? _bus;
    private ISendEndpoint? _sendEndpoint;
    public DeliveryCollector[] Collectors { get; }

    public Driver(Settings settings, bool consumer, int index = 0, IPubSubClient? sharedBus = null)
    {
        _settings = settings;
        Collectors = Enumerable.Range(0, consumer && settings.Framework == "masstransit" && !settings.Broker ? settings.Subscribers : 1)
            .Select(_ => new DeliveryCollector(settings)).ToArray();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(15));
        builder.Services.AddSingleton(Collectors[0]);
        if (settings.Framework == "foundatio")
        {
            // Keep the publisher's local handler list empty: only remote consumer deliveries count.
            var mediator = builder.Services.AddMediator(o =>
            {
                if (settings.Operation == "pubsub" && !consumer) o.AddAssembly<IMediator>();
                else o.AddAssembly<WorkHandlerMarker>();
            });
            if (settings.Transport != "local")
            {
                mediator.ConfigureDistributed(o => o.ResourcePrefix = settings.RunId);
                if (settings.Operation == "queue")
                    mediator.AddDistributedQueues(o =>
                    {
                        o.Workers = consumer ? WorkerSelection.All : WorkerSelection.None;
                        o.QueueDepthPollInterval = TimeSpan.Zero;
                        o.QueueOverrides["work"] = q => { q.Concurrency = settings.Concurrency; q.PrefetchCount = settings.Concurrency; };
                    });
                else
                {
                    if (sharedBus is not null) builder.Services.AddSingleton<IPubSubClient>(new BorrowedBus(sharedBus));
                    mediator.AddDistributedNotifications(o =>
                    {
                        o.Topic = "events";
                        o.MaxCapacity = settings.Capacity;
                        o.ReceiveNotifications = consumer;
                        o.Include<BenchmarkEvent>();
                    });
                }
                if (settings.Broker)
                {
                    // Explicit clients make --region apply identically to both frameworks, including real AWS.
                    builder.Services.AddSingleton<IAmazonSQS>(_ => BrokerResources.CreateSqs(settings));
                    builder.Services.AddSingleton<IAmazonSimpleNotificationService>(_ => BrokerResources.CreateSns(settings));
                    mediator.UseAws(o => { o.Queues.WaitTimeSeconds = 1; o.Notifications.WaitTimeSeconds = 1; });
                }
            }
        }
        else
        {
            builder.Services.Configure<MassTransitHostOptions>(o => { o.WaitUntilStarted = true; o.StartTimeout = TimeSpan.FromSeconds(30); });
            builder.Services.AddMassTransit(x =>
            {
                if (settings.Broker)
                    x.UsingAmazonSqs((_, cfg) =>
                    {
                        cfg.Host(settings.Region, h =>
                        {
                            if (settings.ServiceUrl is not null)
                            {
                                h.Credentials(new BasicAWSCredentials("test", "test"));
                                h.Config(new AmazonSQSConfig { ServiceURL = settings.ServiceUrl, AuthenticationRegion = settings.Region });
                                h.Config(new AmazonSimpleNotificationServiceConfig { ServiceURL = settings.ServiceUrl, AuthenticationRegion = settings.Region });
                            }
                        });
                        // Explicit names also isolate resources with custom/local SDK endpoint configuration.
                        cfg.OverrideDefaultBusEndpointQueueName($"{settings.RunId}-bus-{Guid.NewGuid():N}");
                        cfg.Message<BenchmarkEvent>(m => m.SetEntityName(settings.Topic));
                        cfg.Message<QueueMessage>(m => m.SetEntityName(settings.RunId + "-work-message"));
                        cfg.Message<BenchmarkMessage>(m => m.SetEntityName(settings.RunId + "-base-message"));
                        if (consumer)
                            cfg.ReceiveEndpoint(settings.Operation == "queue" ? settings.Queue : $"{settings.RunId}-subscriber-{index}", e =>
                            {
                                e.PrefetchCount = settings.Concurrency;
                                e.ConcurrentMessageLimit = settings.Concurrency;
                                e.ConfigureConsumeTopology = settings.Operation == "pubsub";
                                ConfigureConsumer(e, Collectors[0]);
                            });
                    });
                else
                    x.UsingInMemory((_, cfg) =>
                    {
                        for (int i = 0; consumer && i < Collectors.Length; i++)
                        {
                            var collector = Collectors[i];
                            cfg.ReceiveEndpoint(settings.Operation == "queue" ? settings.Queue : $"{settings.RunId}-subscriber-{i}", e =>
                            {
                                e.PrefetchCount = settings.Concurrency;
                                e.ConcurrentMessageLimit = settings.Concurrency;
                                ConfigureConsumer(e, collector);
                            });
                        }
                    });
            });
        }
        _host = builder.Build();
        _mediator = _host.Services.GetService<IMediator>();
        _bus = _host.Services.GetService<IBus>();
    }

    private void ConfigureConsumer(IReceiveEndpointConfigurator endpoint, DeliveryCollector collector)
    {
        if (_settings.Operation == "queue") endpoint.Instance(new QueueConsumer(collector));
        else endpoint.Instance(new EventConsumer(collector));
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _host.StartAsync(ct);
        if (_bus is not null)
        {
            if (_settings.Operation == "queue")
                _sendEndpoint = await _bus.GetSendEndpoint(new Uri("queue:" + _settings.Queue));
        }
    }

    public async ValueTask SendAsync(int sequence, bool warmup, long started, long scheduled, string payload, CancellationToken ct)
    {
        var s = _settings;
        if (s.Operation == "pubsub")
        {
            var message = new BenchmarkEvent(s.RunId, sequence, warmup, started, scheduled, payload);
            if (_bus is not null) await _bus.Publish(message, ct);
            else if (s.Transport == "local") await _mediator!.PublishAsync(message, ct);
            else await PublishDynamicAsync(message, ct);
        }
        else if (s.Transport == "local")
        {
            var result = await _mediator!.InvokeAsync<Result>(new LocalMessage(s.RunId, sequence, warmup, started, scheduled, payload), ct);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
        }
        else
        {
            var message = new QueueMessage(s.RunId, sequence, warmup, started, scheduled, payload);
            if (_sendEndpoint is not null) await _sendEndpoint.Send(message, ct);
            else
            {
                var result = await _mediator!.EnqueueAsync(message, ct);
                if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            }
        }
    }

    // The object call uses the publisher-only registry instead of statically binding a local event handler.
    private ValueTask PublishDynamicAsync(object message, CancellationToken ct) => _mediator!.PublishAsync(message, ct);

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _host.StopAsync(timeout.Token);
        }
        finally
        {
            if (_host is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else _host.Dispose();
        }
    }

    private sealed class BorrowedBus(IPubSubClient inner) : IPubSubClient
    {
        public Task PublishAsync(string topic, IReadOnlyList<PubSubEntry> messages, CancellationToken cancellationToken = default) => inner.PublishAsync(topic, messages, cancellationToken);
        public Task<IAsyncDisposable> SubscribeAsync(string topic, Func<PubSubMessage, CancellationToken, Task> handler, CancellationToken cancellationToken = default) => inner.SubscribeAsync(topic, handler, cancellationToken);
        public Task EnsureTopicsAsync(IReadOnlyList<TopicDefinition> topics, CancellationToken cancellationToken = default) => inner.EnsureTopicsAsync(topics, cancellationToken);
    }
}

// Non-static marker for assembly selection.
public sealed class WorkHandlerMarker;

internal sealed class TransportCounters : IDisposable
{
    private readonly MeterListener _listener = new();
    private long _published, _dropped, _processed, _failures;
    public long Published => Interlocked.Read(ref _published);
    public long Dropped => Interlocked.Read(ref _dropped);
    public long Processed => Interlocked.Read(ref _processed);
    public int Failures => checked((int)Interlocked.Read(ref _failures));
    public TransportCounters()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == DistributedMetrics.MeterName && instrument.Name is "notifications.published" or "notifications.dropped" or "queue.messages.processed" or "queue.messages.failed" or "queue.messages.dead_lettered")
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            switch (instrument.Name)
            {
                case "notifications.published": Interlocked.Add(ref _published, value); break;
                case "notifications.dropped": Interlocked.Add(ref _dropped, value); break;
                case "queue.messages.processed": Interlocked.Add(ref _processed, value); break;
                default: Interlocked.Add(ref _failures, value); break;
            }
        });
        _listener.Start();
    }
    public void Dispose() => _listener.Dispose();
}
