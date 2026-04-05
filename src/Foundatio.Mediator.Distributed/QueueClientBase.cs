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
/// <item><b>Optional</b>: <see cref="SendBatchAsync"/> (default loops <see cref="SendAsync"/>),
///   <see cref="DeadLetterAsync"/> (default sends to <c>{queueName}-dead-letter</c> then completes),
///   <see cref="GetQueueStatsAsync"/> (default returns zeroed stats).</item>
/// </list>
/// </remarks>
public abstract class QueueClientBase : IQueueClient
{
    /// <inheritdoc />
    public abstract Task SendAsync(string queueName, QueueEntry entry, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation sends entries one at a time via <see cref="SendAsync"/>.
    /// Override to use transport-native batch APIs for better throughput.
    /// </remarks>
    public virtual async Task SendBatchAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default)
    {
        foreach (var entry in entries)
            await SendAsync(queueName, entry, cancellationToken).ConfigureAwait(false);
    }

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
    /// </remarks>
    public virtual async Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default)
    {
        var dlqName = $"{message.QueueName}-dead-letter";

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

        await SendAsync(dlqName, entry, cancellationToken).ConfigureAwait(false);
        await CompleteAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation is a no-op. Override to pre-create queue infrastructure at startup.
    /// </remarks>
    public virtual Task EnsureQueuesAsync(IReadOnlyList<string> queueNames, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation returns zeroed stats. Override if the transport provides
    /// queue metrics (approximate message count, in-flight count, etc.).
    /// </remarks>
    public virtual Task<QueueStats> GetQueueStatsAsync(string queueName, CancellationToken cancellationToken = default)
        => Task.FromResult(new QueueStats { QueueName = queueName });

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync() => default;
}
