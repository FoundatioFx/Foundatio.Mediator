using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Worker-side middleware for <see cref="QueueLockAttribute"/>. Acquires the lock before the handler
/// runs, renews it while the handler runs, and releases it afterwards. When the lock is held elsewhere
/// the message is completed without running the handler.
/// </summary>
[Middleware(Order = -90, ExplicitOnly = true, Lifetime = MediatorLifetime.Singleton)]
public class QueueLockMiddleware
{
    private readonly IQueueLockProvider? _lockProvider;
    private readonly QueueTopology _topology;
    private readonly ILogger<QueueLockMiddleware> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, QueueLockAttribute?> _settings = new(StringComparer.Ordinal);

    public QueueLockMiddleware(QueueTopology topology, ILogger<QueueLockMiddleware> logger, IQueueLockProvider? lockProvider = null, TimeProvider? timeProvider = null)
    {
        _topology = topology;
        _logger = logger;
        _lockProvider = lockProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
                $"Handler '{handlerInfo.DescriptorId}' uses [QueueLock] but no IQueueLockProvider is registered.");

        var key = settings.Key
            ?? (message as IHaveLockKey)?.LockKey
            ?? $"{queueContext.QueueName}:{queueContext.MessageId}";

        var lifetime = settings.LifetimeSeconds > 0 ? TimeSpan.FromSeconds(settings.LifetimeSeconds) : queueContext.VisibilityTimeout;
        if (lifetime <= TimeSpan.Zero)
            lifetime = TimeSpan.FromSeconds(30);

        var acquireTimeout = TimeSpan.FromSeconds(Math.Max(0, settings.AcquireTimeoutSeconds));

        var queueLock = await _lockProvider.TryAcquireAsync(key, lifetime, acquireTimeout, cancellationToken).ConfigureAwait(false);
        if (queueLock is null)
        {
            _logger.LogInformation("Lock '{LockKey}' is held by another worker; completing message {MessageId} on {QueueName} without running {Handler}",
                key, queueContext.MessageId, queueContext.QueueName, handlerInfo.DescriptorId);

            await queueContext.CompleteAsync(cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }

        using var renewCts = new CancellationTokenSource();
        var renewTask = RenewAsync(queueLock, lifetime, renewCts.Token);

        try
        {
            return await next().ConfigureAwait(false);
        }
        finally
        {
            await renewCts.CancelAsync().ConfigureAwait(false);
            await renewTask.ConfigureAwait(false);
            await queueLock.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RenewAsync(IQueueLock queueLock, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var interval = lifetime * (2.0 / 3.0);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, _timeProvider, cancellationToken).ConfigureAwait(false);
                await queueLock.RenewAsync(lifetime, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to renew lock '{LockKey}'; retrying on the next interval", queueLock.Key);
            }
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
