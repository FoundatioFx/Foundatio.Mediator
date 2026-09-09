using Foundatio.Serializer;
using Foundatio.Messaging;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Channels;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Background service that bridges locally published distributed notifications
/// to a remote <see cref="IMessageBus"/> (outbound) and re-publishes inbound bus messages
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
    private readonly IMessageBus _bus;
    private readonly DistributedNotificationOptions _options;
    private readonly ISerializer _serializer;
    private readonly ILogger<DistributedNotificationWorker> _logger;
    private readonly IMessageTypeRegistry? _typeResolver;

    private readonly ConditionalWeakTable<object, InboundMarker> _inboundMessages = new();
    private sealed class InboundMarker;
    private readonly TaskCompletionSource _outboundReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _inboundReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DistributedInfrastructureReady? _infraReady;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Type, Type> _outboundRoutes = new();
    private Type[] _subscriptionTypes = [];

    public DistributedNotificationWorker(
        IServiceScopeFactory scopeFactory,
        IMessageBus bus,
        DistributedNotificationOptions options,
        ILogger<DistributedNotificationWorker> logger,
        IMessageTypeRegistry? typeResolver = null,
        DistributedInfrastructureReady? infraReady = null,
        TimeProvider? timeProvider = null, ISerializer? serializer = null)
    {
        _scopeFactory = scopeFactory;
        _bus = bus;
        _options = options;
        _serializer = serializer ?? DefaultSerializer.Instance;
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
                FullMode = BoundedChannelFullMode.DropOldest
            };
            if (_infraReady is not null)
                await _infraReady.WaitAsync(stoppingToken).ConfigureAwait(false);
            var types = _options.HasDynamicRules || _options.ResolvedTypes.Any(type => type.IsValueType)
                ? [typeof(object)]
                : _options.ResolvedTypes.Concat(_options.IncludedAssignableTo)
                    .Append(_options.IncludeAllNotifications ? typeof(INotification) : typeof(IDistributedNotification)).Distinct().ToArray();
            _subscriptionTypes = types.Where(type => !types.Any(other => other != type && other.IsAssignableFrom(type))).ToArray();
            using var slots = new SemaphoreSlim(_options.MaxConcurrentPublishes);
            using var subscriptionCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var subscribe = typeof(DistributedNotificationWorker).GetMethod(nameof(RunSubscriptionAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;
            var subscriptions = _subscriptionTypes.Select(type =>
                subscribe.MakeGenericMethod(type).CreateDelegate<Func<IMediator, SubscriberOptions, SemaphoreSlim, CancellationTokenSource, Task>>(this)(mediator, options, slots, subscriptionCancellation)).ToArray();
            foreach (var failed in subscriptions.Where(task => task.IsFaulted))
                await failed.ConfigureAwait(false);
            _outboundReady.TrySetResult();
            await Task.WhenAll(subscriptions).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _outboundReady.TrySetException(ex);
            throw;
        }
    }

    private async Task RunSubscriptionAsync<T>(IMediator mediator, SubscriberOptions options, SemaphoreSlim slots, CancellationTokenSource subscriptionCancellation)
    {
        var stoppingToken = subscriptionCancellation.Token;
        await using var subscription = mediator.SubscribeAsync<MessageContext<T>>(stoppingToken, options).GetAsyncEnumerator(stoppingToken);
        var pending = subscription.MoveNextAsync();
        var active = new List<Task>(_options.MaxConcurrentPublishes);
        try
        {
            while (await pending.ConfigureAwait(false))
            {
                var envelope = subscription.Current;
                object notification = envelope.Message!;
                var messageType = notification.GetType();
                if (_options.ShouldDistribute(messageType) && !_inboundMessages.TryGetValue(notification, out _)
                    && _outboundRoutes.GetOrAdd(messageType, static (type, types) => types.First(subscriptionType => subscriptionType.IsAssignableFrom(type)), _subscriptionTypes) == typeof(T))
                {
                    await slots.WaitAsync(stoppingToken).ConfigureAwait(false);
                    var publication = PublishOutboundAsync(notification, envelope.ActivityContext, slots, stoppingToken);
                    if (!publication.IsCompletedSuccessfully)
                        active.Add(publication);
                    for (int i = active.Count - 1; i >= 0; i--)
                        if (active[i].IsCompleted)
                        {
                            await active[i].ConfigureAwait(false);
                            active.RemoveAt(i);
                        }
                }
                pending = subscription.MoveNextAsync();
            }
        }
        catch
        {
            await subscriptionCancellation.CancelAsync().ConfigureAwait(false);
            throw;
        }
        finally { await Task.WhenAll(active).ConfigureAwait(false); }
    }

    private async Task PublishOutboundAsync(object notification, ActivityContext parentContext, SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        try
        {
            var messageType = notification.GetType();
            var headers = new Dictionary<string, string>
            {
                [KnownHeaders.MessageType] = messageType.FullName!,
                [ExecutionHeaders.OriginNode] = _options.HostId,
                [ExecutionHeaders.EnqueuedAt] = _timeProvider.GetUtcNow().ToString("O")
            };
            using var activity = MediatorActivitySource.Instance.StartActivity($"Publish {messageType.Name}", ActivityKind.Producer, parentContext);
            if (Activity.Current is { } active)
            {
                headers[KnownHeaders.TraceParent] = active.Id!;
                if (active.TraceStateString is { Length: > 0 } traceState)
                    headers[KnownHeaders.TraceState] = traceState;
            }
            activity?.SetTag("messaging.operation.type", "publish");
            activity?.SetTag("messaging.destination.name", _options.EffectiveTopic);
            activity?.SetTag("messaging.message.type", messageType.FullName);
            await _bus.PublishAsync(notification, new MessagePublishOptions { Topic = _options.EffectiveTopic, Headers = Foundatio.Messaging.MessageHeaders.Create(headers) }, stoppingToken)
                .WaitAsync(stoppingToken).ConfigureAwait(false);
            DistributedMetrics.NotificationsPublished.Add(1, new KeyValuePair<string, object?>("message_type", messageType.Name));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish distributed notification {MessageType} to bus", notification.GetType().Name);
        }
        finally { slots.Release(); }
    }

    /// <summary>
    /// Subscribes to the bus topic and re-publishes received messages locally.
    /// </summary>
    private async Task RunInboundLoopAsync(CancellationToken stoppingToken)
    {
        if (_infraReady is not null)
            await _infraReady.WaitAsync(stoppingToken).ConfigureAwait(false);
        if (!_options.ReceiveNotifications)
        {
            _inboundReady.TrySetResult();
            return;
        }

        int attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            IAsyncDisposable? subscription = null;
            try
            {
                subscription = await _bus.SubscribeNodeAsync(ProcessInboundMessageAsync,
                    new MessageNodeSubscriptionOptions { Topic = _options.EffectiveTopic, NodeId = _options.HostId }, stoppingToken).ConfigureAwait(false);

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

    private async Task ProcessInboundMessageAsync(IMessageContext message, CancellationToken cancellationToken)
    {
        // Skip messages from this host (self-delivery prevention)
        if (message.Headers.TryGetValue(ExecutionHeaders.OriginNode, out var originHostId)
            && string.Equals(originHostId, _options.HostId, StringComparison.Ordinal))
        {
            return;
        }

        if (!message.Headers.TryGetValue(KnownHeaders.MessageType, out var typeName) || string.IsNullOrEmpty(typeName))
        {
            _logger.LogWarning("Received bus message without {Header} header, skipping", KnownHeaders.MessageType);
            return;
        }

        // Types registered at startup resolve directly. A concrete type published elsewhere for a handler
        // declared on an interface or base type is loaded only if this node's own rules would distribute it.
        var messageType = _typeResolver?.Resolve(typeName);



        if (messageType is null || !_options.ShouldDistribute(messageType))
        {
            _logger.LogWarning("Cannot resolve type '{TypeName}' from bus message — not registered and not selected by the distribution rules, skipping", typeName);
            return;
        }

        object? notification;
        try
        {
            notification = _serializer.Deserialize(message.Body, messageType);
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
        if (message.Headers.TryGetValue(KnownHeaders.TraceParent, out var traceParent)
            && ActivityContext.TryParse(traceParent, message.Headers.GetValueOrDefault(KnownHeaders.TraceState), out var parsed))
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
