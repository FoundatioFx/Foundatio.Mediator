using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Worker-side middleware for <see cref="QueueLockAttribute"/>. Acquires the lock before the handler
/// runs, renews it while the handler runs, and releases it afterwards. When the lock is held elsewhere
/// the worker waits with bounded jitter while retaining its queue lease. Distinct work is never discarded.
/// </summary>
[Middleware(Order = -90, ExplicitOnly = true, Lifetime = MediatorLifetime.Singleton)]
public class QueueLockMiddleware
{
    private readonly IQueueLockProvider? _lockProvider;
    private readonly QueueTopology _topology;
    private readonly ILogger<QueueLockMiddleware> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, QueueLockAttribute?> _settings = new(StringComparer.Ordinal);

    public QueueLockMiddleware(QueueTopology topology, ILogger<QueueLockMiddleware> logger, IQueueClient queueClient, IQueueLockProvider? lockProvider = null, TimeProvider? timeProvider = null)
    {
        _topology = topology;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        // A process-local lock is only safe when the queue is process-local too.
        _lockProvider = lockProvider ?? (!queueClient.IsDistributed ? new InMemoryQueueLockProvider(_timeProvider) : null);
    }

    public async ValueTask<object?> ExecuteAsync(
        object message,
        HandlerExecutionDelegate next,
        HandlerExecutionInfo handlerInfo,
        CallContext? callContext,
        CancellationToken cancellationToken)
    {
        // Only the worker side holds a QueueContext; the enqueue side never reaches this middleware.
        if (callContext?.TryGet<QueueContext>(out var queueContext) != true || queueContext is null)
            return await next().ConfigureAwait(false);

        var settings = _settings.GetOrAdd(handlerInfo.DescriptorId, FindSettings);
        if (settings is null)
            return await next().ConfigureAwait(false);

        if (_lockProvider is null)
            throw new InvalidOperationException(
                $"Handler '{handlerInfo.DescriptorId}' uses [QueueLock] but no IQueueLockProvider is registered. " +
                "Register one every worker shares (Redis, a database); the in-memory lock is only used with the in-memory queue.");

        var key = settings.Key
            ?? (message as IHaveLockKey)?.GetLockKey()
            ?? $"{queueContext.QueueName}:{queueContext.MessageId}";

        var lifetime = settings.LifetimeSeconds > 0 ? TimeSpan.FromSeconds(settings.LifetimeSeconds) : queueContext.VisibilityTimeout;
        if (lifetime <= TimeSpan.Zero)
            lifetime = TimeSpan.FromSeconds(30);

        var acquireTimeout = TimeSpan.FromSeconds(Math.Max(0, settings.AcquireTimeoutSeconds));

        IQueueLock? queueLock = null;
        bool reportedContention = false;
        while (queueLock is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            queueLock = await QueueOperation.RunAsync(
                ct => _lockProvider.TryAcquireAsync(key, lifetime, acquireTimeout, ct),
                acquireTimeout + TimeSpan.FromSeconds(5), _timeProvider, cancellationToken).ConfigureAwait(false);
            if (queueLock is not null)
                break;
            if (!reportedContention)
            {
                reportedContention = true;
                DistributedMetrics.Deferred.Add(1, DistributedMetrics.Tags(queueContext.QueueName, queueContext.MessageType?.Name, null, "lock-contention"));
                _logger.LogDebug("Waiting for lock for message {MessageId} on {QueueName}", queueContext.MessageId, queueContext.QueueName);
            }
            // Keep the same delivery and retry budget. Re-enqueueing here would consume attempts,
            // or require a non-atomic copy/delete operation that could duplicate distinct work.
            await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 501)), _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        using var renewCts = new CancellationTokenSource();
        var renewTask = RenewAsync(queueLock, lifetime, queueContext, renewCts.Token);

        try
        {
            return await next().ConfigureAwait(false);
        }
        finally
        {
            await renewCts.CancelAsync().ConfigureAwait(false);
            await renewTask.ConfigureAwait(false);
            await queueLock.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), _timeProvider).ConfigureAwait(false);
        }
    }

    private async Task RenewAsync(IQueueLock queueLock, TimeSpan lifetime, QueueContext context, CancellationToken cancellationToken)
    {
        var expires = _timeProvider.GetUtcNow() + lifetime;
        bool retry = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            var remaining = expires - _timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                break;
            try
            {
                var delay = retry ? TimeSpan.FromTicks(Math.Min(TimeSpan.TicksPerSecond, remaining.Ticks / 4)) : remaining / 2;
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                remaining = expires - _timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                    break;
                var started = _timeProvider.GetUtcNow();
                await QueueOperation.RunAsync(ct => queueLock.RenewAsync(lifetime, ct), remaining, _timeProvider, cancellationToken).ConfigureAwait(false);
                expires = started + lifetime;
                retry = false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (QueueLeaseLostException) { break; }
            catch (Exception ex)
            {
                retry = true;
                _logger.LogWarning(ex, "Lock renewal failed on {QueueName}; retrying within the remaining lease", context.QueueName);
            }
        }
        if (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Lock lost for message {MessageId} on {QueueName}; cancelling processing", context.MessageId, context.QueueName);
            context.OnCancelProcessing?.Invoke();
        }
    }

    private QueueLockAttribute? FindSettings(string descriptorId)
    {
        var registration = _topology.GetByDescriptorId(descriptorId);
        if (registration is null)
            return null;

        foreach (var handler in registration.Handlers)
        {
            if (string.Equals(handler.DescriptorId, descriptorId, StringComparison.Ordinal))
                return handler.GetPreferredAttribute<QueueLockAttribute>()?.Attribute as QueueLockAttribute;
        }

        return null;
    }
}
