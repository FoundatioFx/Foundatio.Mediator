namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Configuration for a single <see cref="QueueWorker"/>, resolved from the <see cref="QueueAttribute"/>
/// shared by every handler on the queue.
/// </summary>
public class QueueWorkerOptions
{
    /// <summary>
    /// The fully prefixed queue name.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>
    /// The declared message type of the queue's handlers. Used as the deserialization fallback when a
    /// message carries no <see cref="MessageHeaders.MessageType"/> header.
    /// </summary>
    public required Type MessageType { get; init; }

    /// <summary>
    /// Every handler registration bound to this queue. A received message is dispatched to each
    /// registration whose message type is assignable from the message's concrete type.
    /// </summary>
    public required IReadOnlyList<HandlerRegistration> Registrations { get; init; }

    /// <summary>
    /// Number of messages processed concurrently by this worker.
    /// </summary>
    public int Concurrency { get; init; } = 1;

    /// <summary>
    /// Number of messages fetched per receive call.
    /// </summary>
    public int PrefetchCount { get; init; } = 1;

    /// <summary>
    /// Visibility timeout requested on receive and renewed halfway through the lease while a handler runs.
    /// </summary>
    public TimeSpan VisibilityTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Total attempts before a message is dead-lettered. Negative means unlimited.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Worker group for selective hosting.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// Whether the worker completes or abandons based on the handler outcome.
    /// </summary>
    public bool AutoComplete { get; init; } = true;

    /// <summary>
    /// Whether the worker renews the visibility timeout while the handler runs.
    /// </summary>
    public bool AutoRenewTimeout { get; init; } = true;

    /// <summary>
    /// Delay strategy between retries.
    /// </summary>
    public QueueRetryPolicy RetryPolicy { get; init; } = QueueRetryPolicy.Exponential;

    /// <summary>
    /// Base delay for <see cref="QueueRetryPolicy.Fixed"/> and <see cref="QueueRetryPolicy.Exponential"/>.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Explicit per-attempt delays for <see cref="QueueRetryPolicy.Schedule"/>; the last entry repeats.
    /// </summary>
    public IReadOnlyList<TimeSpan>? RetrySchedule { get; init; }

    /// <summary>
    /// Whether job state is tracked for messages on this queue.
    /// </summary>
    public bool TrackProgress { get; init; }

    /// <summary>
    /// How often the worker polls the job state store for a cancellation request. Default is 5 seconds.
    /// </summary>
    public TimeSpan CancellationPollInterval { get; init; } = TimeSpan.FromSeconds(5);
}
