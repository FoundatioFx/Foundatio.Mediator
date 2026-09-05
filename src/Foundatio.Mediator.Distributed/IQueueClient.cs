namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Transport-agnostic durable queue abstraction with at-least-once delivery semantics.
/// </summary>
/// <remarks>
/// A received message stays invisible to other consumers for its visibility timeout. The consumer
/// must <see cref="CompleteAsync"/> it, <see cref="AbandonAsync"/> it for redelivery, or
/// <see cref="DeadLetterAsync"/> it; otherwise the transport redelivers it when the timeout lapses.
/// </remarks>
public interface IQueueClient : IAsyncDisposable
{
    /// <summary>
    /// Sends one or more messages to the queue.
    /// </summary>
    Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives up to <paramref name="maxCount"/> messages, locking each for <paramref name="visibilityTimeout"/>.
    /// A <c>null</c> timeout uses the queue's configured default.
    /// </summary>
    Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, TimeSpan? visibilityTimeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives up to <paramref name="maxCount"/> messages using the queue's default visibility timeout.
    /// </summary>
    Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, CancellationToken cancellationToken = default)
        => ReceiveAsync(queueName, maxCount, null, cancellationToken);

    /// <summary>
    /// Removes a successfully processed message from the queue.
    /// </summary>
    Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a message to the queue so it is redelivered after <paramref name="delay"/>.
    /// </summary>
    Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends the visibility timeout of an in-flight message by <paramref name="extension"/>.
    /// </summary>
    Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a message to the queue's dead-letter queue with <see cref="MessageHeaders.DeadLetterReason"/>
    /// and related headers, then completes the original.
    /// </summary>
    Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the queues (and their dead-letter queues) exist with the requested settings.
    /// </summary>
    Task EnsureQueuesAsync(IReadOnlyList<QueueDefinition> queues, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Returns approximate depth statistics for the queues.
    /// </summary>
    Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(IReadOnlyList<string> queueNames, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<QueueStats>>(queueNames.Select(n => new QueueStats { QueueName = n }).ToList());

    /// <summary>
    /// Receives messages from the dead-letter queue of <paramref name="queueName"/>. Received messages are
    /// locked like any other receive, so callers must complete, abandon, or <see cref="ReplayAsync"/> them.
    /// </summary>
    Task<IReadOnlyList<QueueMessage>> ReceiveDeadLettersAsync(string queueName, int maxCount, CancellationToken cancellationToken = default)
        => ReceiveAsync(QueueDefinition.DeadLetterQueueNameFor(queueName), maxCount, null, cancellationToken);

    /// <summary>
    /// Receives dead letters, waiting at most <paramref name="waitTime"/> for messages when the queue is empty.
    /// Transports that long-poll should bound the wait on the server: cancelling a receive client-side leaves
    /// the server-side poll running, and it can grab a message another caller releases moments later.
    /// </summary>
    async Task<IReadOnlyList<QueueMessage>> ReceiveDeadLettersAsync(string queueName, int maxCount, TimeSpan waitTime, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(waitTime);
        try
        {
            return await ReceiveDeadLettersAsync(queueName, maxCount, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    /// <summary>
    /// Sends a dead-lettered message back to its original queue (from <see cref="MessageHeaders.OriginalQueueName"/>)
    /// without the dead-letter headers, then completes the dead-letter copy.
    /// </summary>
    async Task ReplayAsync(QueueMessage deadLetter, CancellationToken cancellationToken = default)
    {
        if (!deadLetter.Headers.TryGetValue(MessageHeaders.OriginalQueueName, out var originalQueue) || string.IsNullOrEmpty(originalQueue))
            throw new InvalidOperationException($"Message {deadLetter.Id} has no {MessageHeaders.OriginalQueueName} header and cannot be replayed.");

        var headers = new Dictionary<string, string>(deadLetter.Headers);
        headers.Remove(MessageHeaders.DeadLetterReason);
        headers.Remove(MessageHeaders.DeadLetteredAt);
        headers.Remove(MessageHeaders.OriginalQueueName);
        headers.Remove(MessageHeaders.DeadLetterDequeueCount);
        headers[MessageHeaders.ReplayedAt] = DateTimeOffset.UtcNow.ToString("O");

        await SendAsync(originalQueue, [new QueueEntry { Body = deadLetter.Body, Headers = headers }], cancellationToken).ConfigureAwait(false);
        await CompleteAsync(deadLetter, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    ValueTask IAsyncDisposable.DisposeAsync() => default;
}
