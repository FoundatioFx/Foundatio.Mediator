using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
/// Publications are buffered with DropOldest and are best effort. StartAsync waits until the
/// local subscription and transport subscription are established. Inbound identities are weakly
/// tracked for their lifetime so an overflow or handler failure cannot create a rebroadcast loop.
/// </remarks>
public sealed class DistributedNotificationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPubSubClient _bus;
    private readonly DistributedNotificationOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<DistributedNotificationWorker> _logger;
    private readonly MessageTypeResolver? _typeResolver;

    private readonly ConditionalWeakTable<object, InboundMarker> _inboundMessages = new();
    private sealed class InboundMarker;
    private readonly TaskCompletionSource _outboundReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _inboundReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _droppedCount;
    private long _lastDropWarningTicks;

    /// <summary>Number of outbound notifications evicted from this worker's bounded buffer.</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

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

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(_outboundReady.Task, _inboundReady.Task).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(RunOutboundLoopAsync(stoppingToken), RunInboundLoopAsync(stoppingToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _outboundReady.TrySetException(ex);
            _inboundReady.TrySetException(ex);
            throw;
        }
        finally
        {
            _outboundReady.TrySetCanceled(stoppingToken);
            _inboundReady.TrySetCanceled(stoppingToken);
        }
    }

    private async Task RunOutboundLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var options = new SubscriberOptions
            {
                MaxCapacity = _options.MaxCapacity,
                FullMode = BoundedChannelFullMode.DropOldest,
                Filter = message => _options.ShouldDistribute(message.GetType()) && !_inboundMessages.TryGetValue(message, out _),
                OnDropped = OnDropped
            };
            if (_infraReady is not null)
                await _infraReady.WaitAsync(stoppingToken).ConfigureAwait(false);
            await using var subscription = mediator.SubscribeAsync<MessageContext<object>>(stoppingToken, options).GetAsyncEnumerator(stoppingToken);
            var pending = subscription.MoveNextAsync();
            _outboundReady.TrySetResult();
            if (_options.MaxConcurrentPublishes == 1)
            {
                while (await pending.ConfigureAwait(false))
                {
                    await PublishOutboundAsync(subscription.Current, stoppingToken).ConfigureAwait(false);
                    pending = subscription.MoveNextAsync();
                }
            }
            else
                await Parallel.ForEachAsync(ReadAsync(), new ParallelOptions
                {
                    MaxDegreeOfParallelism = _options.MaxConcurrentPublishes,
                    CancellationToken = stoppingToken
                }, (envelope, ct) => new ValueTask(PublishOutboundAsync(envelope, ct))).ConfigureAwait(false);

            async IAsyncEnumerable<MessageContext<object>> ReadAsync()
            {
                while (await pending.ConfigureAwait(false))
                {
                    yield return subscription.Current;
                    pending = subscription.MoveNextAsync();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _outboundReady.TrySetException(ex);
            throw;
        }
    }

    private void OnDropped(object item)
    {
        Interlocked.Increment(ref _droppedCount);
        var notification = ((MessageContext<object>)item).Message;
        DistributedMetrics.NotificationsDropped.Add(1, new KeyValuePair<string, object?>("message_type", notification.GetType().Name));
        var now = _timeProvider.GetUtcNow().UtcTicks;
        var previous = Interlocked.Read(ref _lastDropWarningTicks);
        if ((previous == 0 || now - previous >= TimeSpan.TicksPerMinute)
            && Interlocked.CompareExchange(ref _lastDropWarningTicks, now, previous) == previous)
            _logger.LogWarning("Distributed notification buffer is full; oldest notifications are being dropped. Total dropped: {DroppedCount}. Use a queue subscription when delivery must be durable.", DroppedCount);
    }

    private async Task PublishOutboundAsync(MessageContext<object> envelope, CancellationToken stoppingToken)
    {
        var notification = envelope.Message;
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
            using var activity = MediatorActivitySource.Instance.StartActivity($"Publish {messageType.Name}", ActivityKind.Producer, envelope.ActivityContext);
            if (Activity.Current is { } active)
            {
                headers[MessageHeaders.TraceParent] = active.Id!;
                if (active.TraceStateString is { Length: > 0 } traceState)
                    headers[MessageHeaders.TraceState] = traceState;
            }
            activity?.SetTag("messaging.operation.type", "publish");
            activity?.SetTag("messaging.destination.name", _options.EffectiveTopic);
            activity?.SetTag("messaging.message.type", messageType.FullName);
            await _bus.PublishAsync(_options.EffectiveTopic, [new PubSubEntry { Body = body, Headers = headers }], stoppingToken)
                .WaitAsync(stoppingToken).ConfigureAwait(false);
            DistributedMetrics.NotificationsPublished.Add(1, new KeyValuePair<string, object?>("message_type", messageType.Name));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish distributed notification {MessageType} to bus", notification.GetType().Name);
        }
    }

    /// <summary>
    /// Subscribes to the bus topic and re-publishes received messages locally.
    /// </summary>
    private async Task RunInboundLoopAsync(CancellationToken stoppingToken)
    {
        if (_infraReady is not null)
            await _infraReady.WaitAsync(stoppingToken).ConfigureAwait(false);
        int attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            IAsyncDisposable? subscription = null;
            try
            {
                subscription = await _bus.SubscribeAsync(_options.EffectiveTopic, async (message, ct) =>
                {
                    await ProcessInboundMessageAsync(message, ct).ConfigureAwait(false);
                }, stoppingToken).ConfigureAwait(false);

                _inboundReady.TrySetResult();
                attempt = 0;
                _logger.LogInformation("Subscribed to notification topic {Topic}", _options.EffectiveTopic);

                // Keep alive until cancellation
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Math.Min(attempt, 6)), 60));
                _logger.LogError(ex, "Failed to subscribe to notification topic {Topic}; this node will not receive notifications until it succeeds. Retrying in {Delay}",
                    _options.EffectiveTopic, delay);

                try { await Task.Delay(delay, _timeProvider, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
            finally
            {
                if (subscription is not null)
                    await subscription.DisposeAsync().ConfigureAwait(false);
            }
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

        // Types registered at startup resolve directly. A concrete type published elsewhere for a handler
        // declared on an interface or base type is loaded only if this node's own rules would distribute it.
        var messageType = _typeResolver?.TryResolve(typeName);
        if (messageType is null && _typeResolver?.TryResolve(typeName, typeof(object)) is { } candidate && _options.ShouldDistribute(candidate))
            messageType = candidate;

        if (messageType is null || !_options.ShouldDistribute(messageType))
        {
            _logger.LogWarning("Cannot resolve type '{TypeName}' from bus message — not registered and not selected by the distribution rules, skipping", typeName);
            return;
        }

        object? notification;
        try
        {
            notification = JsonSerializer.Deserialize(message.Body.Span, messageType, _jsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to deserialize bus message as {TypeName}", typeName);
            return;
        }

        if (notification is null)
        {
            _logger.LogWarning("Deserialized bus message as {TypeName} was null, skipping", typeName);
            return;
        }

        // A weak marker survives a buffer drop or a partially failed local publish without retaining the message.
        _inboundMessages.Add(notification, new InboundMarker());
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
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", _options.EffectiveTopic);
        activity?.SetTag("messaging.message.type", messageType.FullName);

        DistributedMetrics.NotificationsReceived.Add(1, new KeyValuePair<string, object?>("message_type", messageType.Name));

        // The originating node already enqueued any [Queue] handlers for this notification;
        // QueueMiddleware checks this scope so they are not enqueued again here.
        using var distributedScope = DistributedContext.BeginNotificationScope(notification);

        // Create a scope per inbound message for proper scoped service lifetime
        await using var scope = _scopeFactory.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Publish skips auth automatically via the publish delegate path
        await mediator.PublishAsync(notification, cancellationToken).ConfigureAwait(false);
    }
}
