using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Optional base class for <see cref="IQueueClient"/> implementations that provides
/// defaults for dead-lettering, provisioning, and statistics. Transports override the
/// core send, receive, complete, abandon, and renew operations.
/// </summary>
public abstract class QueueClientBase : IQueueClient
{
    /// <summary>
    /// Logger available for derived classes. Defaults to <see cref="NullLogger.Instance"/>.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Clock used for dead-letter timestamps. Defaults to <see cref="TimeProvider.System"/>.
    /// </summary>
    protected TimeProvider TimeProvider { get; }

    protected QueueClientBase(ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        Logger = logger ?? NullLogger.Instance;
        TimeProvider = timeProvider ?? System.TimeProvider.System;
    }

    /// <inheritdoc />
    public abstract Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, TimeSpan? visibilityTimeout, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, CancellationToken cancellationToken = default)
        => ReceiveAsync(queueName, maxCount, null, cancellationToken);

    /// <inheritdoc />
    public abstract Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public virtual Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// Sends the original body and headers plus the dead-letter headers to
    /// <see cref="QueueDefinition.DeadLetterQueueNameFor"/>, then completes the original.
    /// </remarks>
    public virtual async Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default)
    {
        var dlqName = QueueDefinition.DeadLetterQueueNameFor(message.QueueName);

        var headers = new Dictionary<string, string>(message.Headers)
        {
            [MessageHeaders.DeadLetterReason] = reason,
            [MessageHeaders.DeadLetteredAt] = TimeProvider.GetUtcNow().ToString("O"),
            [MessageHeaders.OriginalQueueName] = message.QueueName,
            [MessageHeaders.DeadLetterDequeueCount] = message.DequeueCount.ToString()
        };

        await SendAsync(dlqName, [new QueueEntry { Body = message.Body, Headers = headers }], cancellationToken).ConfigureAwait(false);
        await CompleteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual Task EnsureQueuesAsync(IReadOnlyList<QueueDefinition> queues, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(IReadOnlyList<string> queueNames, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<QueueStats>>(queueNames.Select(n => new QueueStats { QueueName = n }).ToList());

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync() => default;
}
