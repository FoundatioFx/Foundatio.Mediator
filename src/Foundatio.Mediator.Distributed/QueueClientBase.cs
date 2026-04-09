using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Optional base class for <see cref="IQueueClient"/> implementations that provides
/// sensible defaults for common operations. Implementations only need to override the
/// core transport-specific methods.
/// </summary>
/// <remarks>
/// <para>Override guidance for new transport implementations:</para>
/// <list type="bullet">
/// <item><b>Required</b>: <see cref="SendAsync"/>, <see cref="ReceiveAsync"/>,
///   <see cref="CompleteAsync"/>, <see cref="AbandonAsync"/>.</item>
/// <item><b>Recommended</b>: <see cref="RenewTimeoutAsync"/> (if the transport supports visibility timeouts),
///   <see cref="EnsureQueuesAsync"/> (if infrastructure pre-creation is beneficial).</item>
/// <item><b>Optional</b>:
///   <see cref="DeadLetterAsync"/> (default sends to <c>{queueName}-dead-letter</c> then completes),
///   <see cref="GetQueueStatsAsync"/> (default returns zeroed stats).</item>
/// </list>
/// </remarks>
public abstract class QueueClientBase : IQueueClient
{
    /// <summary>
    /// Logger available for derived classes. Defaults to <see cref="NullLogger.Instance"/>.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="QueueClientBase"/>.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics. Defaults to <see cref="NullLogger.Instance"/>.</param>
    protected QueueClientBase(ILogger? logger = null)
    {
        Logger = logger ?? NullLogger.Instance;
    }
    /// <inheritdoc />
    public abstract Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation is a no-op. Override for transports that support
    /// visibility timeouts (e.g., SQS ChangeMessageVisibility).
    /// </remarks>
    public virtual Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation sends the original message (with dead-letter metadata headers)
    /// to <c>{queueName}-dead-letter</c>, then completes the original message.
    /// Override if the transport has native dead-letter support (e.g., Azure Service Bus).
    /// <para>
    /// <b>Important:</b> Transport implementations that do not support dead-letter queues
    /// should override this method. The default behaviour discards the message after
    /// sending it to a DLQ queue name that may not exist for the transport.
    /// </para>
    /// </remarks>
    public virtual async Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default)
    {
        var dlqName = $"{message.QueueName}-dead-letter";

        Logger.LogWarning(
            "Using default DeadLetterAsync for queue {QueueName}: forwarding to {DlqName}. " +
            "Override DeadLetterAsync to use transport-native dead-letter support.",
            message.QueueName, dlqName);

        var headers = new Dictionary<string, string>(message.Headers)
        {
            [MessageHeaders.DeadLetterReason] = reason,
            [MessageHeaders.DeadLetteredAt] = DateTimeOffset.UtcNow.ToString("O"),
            [MessageHeaders.OriginalQueueName] = message.QueueName,
            [MessageHeaders.DeadLetterDequeueCount] = message.DequeueCount.ToString()
        };

        var entry = new QueueEntry
        {
            Body = message.Body,
            Headers = headers
        };

        await SendAsync(dlqName, [entry], cancellationToken).ConfigureAwait(false);
        await CompleteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation is a no-op. Override to pre-create queue infrastructure at startup.
    /// </remarks>
    public virtual Task EnsureQueuesAsync(IReadOnlyList<QueueDefinition> queues, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation returns zeroed stats. Override if the transport provides
    /// queue metrics (approximate message count, in-flight count, etc.).
    /// </remarks>
    public virtual Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(IReadOnlyList<string> queueNames, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<QueueStats>>(queueNames.Select(n => new QueueStats { QueueName = n }).ToList());

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync() => default;
}
