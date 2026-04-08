using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Background service that bridges locally published distributed notifications
/// to a remote <see cref="IPubSubClient"/> (outbound) and re-publishes inbound bus messages
/// to the local mediator.
/// </summary>
/// <remarks>
/// <para><b>Outbound loop</b>: uses <c>mediator.SubscribeAsync&lt;MessageContext&lt;object&gt;&gt;()</c>
/// to tap into all locally published notifications, filters to types that should be distributed
/// (via <see cref="DistributedNotificationOptions.ShouldDistribute"/>), then serializes and publishes
/// them to the pub/sub client. Messages that arrived from the bus (tracked by reference identity
/// in <see cref="_inboundMessages"/>) are skipped to prevent re-broadcast loops.</para>
///
/// <para><b>Inbound loop</b>: subscribes to the bus topic and, for each received message,
/// checks the <see cref="MessageHeaders.OriginHostId"/> header. If it matches this host's ID the
/// message is skipped (self-delivery). Otherwise the message is deserialized, added to the
/// <see cref="_inboundMessages"/> set, and published locally via <c>mediator.PublishAsync()</c>.
/// The reference set entry is removed in a finally block.</para>
/// </remarks>
public sealed class DistributedNotificationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPubSubClient _bus;
    private readonly DistributedNotificationOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<DistributedNotificationWorker> _logger;
    private readonly MessageTypeResolver? _typeResolver;

    /// <summary>
    /// Tracks notification objects that arrived from the bus and are currently being
    /// re-published locally. The outbound loop checks this set by reference identity
    /// and skips any match, preventing infinite re-broadcast.
    /// </summary>
    private readonly ConcurrentDictionary<object, byte> _inboundMessages = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Safety cap for <see cref="_inboundMessages"/>. Under normal operation the outbound
    /// loop removes entries quickly, but if it stalls this prevents unbounded memory growth.
    /// </summary>
    private const int MaxInboundTrackingEntries = 10_000;

    private readonly DistributedInfrastructureReady? _infraReady;
    private readonly TimeProvider _timeProvider;

    public DistributedNotificationWorker(
        IServiceScopeFactory scopeFactory,
        IPubSubClient bus,
        DistributedNotificationOptions options,
        ILogger<DistributedNotificationWorker> logger,
        MessageTypeResolver? typeResolver = null,
        DistributedInfrastructureReady? infraReady = null,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _bus = bus;
        _options = options;
        _jsonOptions = options.JsonSerializerOptions ?? JsonSerializerOptions.Default;
        _logger = logger;
        _typeResolver = typeResolver;
        _infraReady = infraReady;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for topics to be created before subscribing
        if (_infraReady is not null)
        {
            try { await _infraReady.WaitAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }

        _logger.LogInformation(
            "Distributed notification worker starting (HostId={HostId}, Topic={Topic})",
            _options.HostId, _options.EffectiveTopic);

        var outboundTask = RunOutboundLoopAsync(stoppingToken);
        var inboundTask = RunInboundLoopAsync(stoppingToken);

        await Task.WhenAll(outboundTask, inboundTask).ConfigureAwait(false);

        _logger.LogInformation("Distributed notification worker stopped");
    }

    /// <summary>
    /// Reads from the local mediator subscription stream and publishes to the bus.
    /// </summary>
    private async Task RunOutboundLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Create a long-lived scope for the outbound subscription stream
            await using var scope = _scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var subscriberOptions = new SubscriberOptions
            {
                MaxCapacity = _options.MaxCapacity,
                FullMode = _options.FullMode
            };

            await foreach (var envelope in mediator.SubscribeAsync<MessageContext<object>>(stoppingToken, subscriberOptions).ConfigureAwait(false))
            {
                var notification = envelope.Message;

                // Filter to only types that should be distributed
                if (!_options.ShouldDistribute(notification.GetType()))
                    continue;

                // Skip messages that arrived from the bus. TryRemove atomically checks and cleans
                // up the tracking entry, avoiding the race where a finally block removed the entry
                // before this loop had a chance to read from the channel.
                if (_inboundMessages.TryRemove(notification, out _))
                    continue;

                try
                {
                    var messageType = notification.GetType();
                    var body = JsonSerializer.SerializeToUtf8Bytes(notification, messageType, _jsonOptions);

                    var headers = new Dictionary<string, string>
                    {
                        [MessageHeaders.MessageType] = messageType.FullName!,
                        [MessageHeaders.OriginHostId] = _options.HostId,
                        [MessageHeaders.PublishedAt] = _timeProvider.GetUtcNow().ToString("O")
                    };

                    // Start a producer activity parented to the original publisher's trace
                    // (e.g. the HTTP request handler) so the SNS.Publish span is in the same trace.
                    using var activity = MediatorActivitySource.Instance.StartActivity(
                        $"Publish {messageType.Name}",
                        ActivityKind.Producer,
                        envelope.ActivityContext);

                    // Propagate W3C trace context so downstream consumers appear in the same trace
                    var activeActivity = Activity.Current;
                    if (activeActivity is not null)
                    {
                        headers[MessageHeaders.TraceParent] = activeActivity.Id!;
                        if (activeActivity.TraceStateString is { Length: > 0 } traceState)
                            headers[MessageHeaders.TraceState] = traceState;
                    }

                    await _bus.PublishAsync(_options.EffectiveTopic, new PubSubEntry { Body = body, Headers = headers }, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish distributed notification {MessageType} to bus",
                        notification.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    /// <summary>
    /// Subscribes to the bus topic and re-publishes received messages locally.
    /// </summary>
    private async Task RunInboundLoopAsync(CancellationToken stoppingToken)
    {
        IAsyncDisposable? subscription = null;
        try
        {
            subscription = await _bus.SubscribeAsync(_options.EffectiveTopic, async (message, ct) =>
            {
                await ProcessInboundMessageAsync(message, ct).ConfigureAwait(false);
            }, stoppingToken).ConfigureAwait(false);

            // Keep alive until cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        finally
        {
            if (subscription is not null)
                await subscription.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ProcessInboundMessageAsync(PubSubMessage message, CancellationToken cancellationToken)
    {
        // Skip messages from this host (self-delivery prevention)
        if (message.Headers.TryGetValue(MessageHeaders.OriginHostId, out var originHostId)
            && string.Equals(originHostId, _options.HostId, StringComparison.Ordinal))
        {
            return;
        }

        if (!message.Headers.TryGetValue(MessageHeaders.MessageType, out var typeName) || string.IsNullOrEmpty(typeName))
        {
            _logger.LogWarning("Received bus message without {Header} header, skipping", MessageHeaders.MessageType);
            return;
        }

        var messageType = _typeResolver?.TryResolve(typeName);
        if (messageType is null)
        {
            _logger.LogWarning("Cannot resolve type '{TypeName}' from bus message — type not registered in MessageTypeResolver, skipping", typeName);
            return;
        }

        object? notification;
        try
        {
            notification = JsonSerializer.Deserialize(message.Body.Span, messageType, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize bus message as {TypeName}", typeName);
            return;
        }

        if (notification is null)
        {
            _logger.LogWarning("Deserialized bus message as {TypeName} was null, skipping", typeName);
            return;
        }

        // Mark by reference so the outbound loop skips this message.
        // Removal happens in the outbound loop (TryRemove) to avoid a race where this
        // finally block runs before the outbound loop reads from the channel.
        if (_inboundMessages.Count >= MaxInboundTrackingEntries)
        {
            _logger.LogWarning(
                "Inbound message tracking dictionary exceeded {MaxEntries} entries — clearing to prevent unbounded growth. " +
                "This may briefly allow a re-broadcast of an in-flight notification.",
                MaxInboundTrackingEntries);
            _inboundMessages.Clear();
        }

        _inboundMessages.TryAdd(notification, 0);
        bool published = false;
        try
        {
            // Restore trace context from the publishing node so this processing
            // appears as a child span of the original operation
            ActivityContext parentContext = default;
            if (message.Headers.TryGetValue(MessageHeaders.TraceParent, out var traceParent)
                && ActivityContext.TryParse(traceParent, message.Headers.GetValueOrDefault(MessageHeaders.TraceState), out var parsed))
            {
                parentContext = parsed;
            }

            using var activity = MediatorActivitySource.Instance.StartActivity(
                $"Process {messageType.Name}",
                ActivityKind.Consumer,
                parentContext);

            // Mark the scope as an inbound notification so middleware (e.g., QueueMiddleware)
            // skips re-enqueueing — the originating node already enqueued to shared infra.
            using var distributedScope = DistributedContext.BeginNotificationScope();

            // Create a scope per inbound message for proper scoped service lifetime
            await using var scope = _scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // Publish skips auth automatically via the publish delegate path
            await mediator.PublishAsync(notification, cancellationToken).ConfigureAwait(false);
            published = true;
        }
        finally
        {
            // Only clean up here if publish failed — the outbound loop will never see the
            // message, so we must remove the tracking entry ourselves.
            if (!published)
                _inboundMessages.TryRemove(notification, out _);
        }
    }
}
