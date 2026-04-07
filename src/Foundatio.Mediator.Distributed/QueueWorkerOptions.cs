namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Configuration for a single queue worker instance.
/// Built from <see cref="QueueAttribute"/> properties during DI registration,
/// with support for programmatic overrides.
/// </summary>
public class QueueWorkerOptions
{
    /// <summary>
    /// The name of the queue to process.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>
    /// The CLR type of the message this worker processes.
    /// </summary>
    public required Type MessageType { get; init; }

    /// <summary>
    /// The handler registration for dispatching messages.
    /// </summary>
    public required HandlerRegistration Registration { get; init; }

    /// <summary>
    /// Number of concurrent consumer tasks. Default is 1.
    /// </summary>
    public int Concurrency { get; init; } = 1;

    /// <summary>
    /// Number of messages to fetch per receive batch. Default is 1.
    /// </summary>
    public int PrefetchCount { get; init; } = 1;

    /// <summary>
    /// How long a message remains invisible after dequeue before being
    /// redelivered. Handlers can extend this via <see cref="QueueContext.RenewTimeoutAsync"/>.
    /// Default is 5 minutes.
    /// </summary>
    public TimeSpan VisibilityTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of times the message will be attempted before dead-lettering.
    /// Default is 3 (1 initial attempt + 2 retries).
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Queue group for selective hosting. When set, this worker only starts
    /// if the host is configured for the matching group.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// When true, the worker automatically completes the message on success
    /// and abandons it on exception. Default is true.
    /// </summary>
    public bool AutoComplete { get; init; } = true;

    /// <summary>
    /// When true, the worker automatically renews the message visibility timeout
    /// on a background timer. Default is true.
    /// </summary>
    public bool AutoRenewTimeout { get; init; } = true;

    /// <summary>
    /// The retry delay strategy for failed messages. Default is <see cref="QueueRetryPolicy.Exponential"/>.
    /// </summary>
    public QueueRetryPolicy RetryPolicy { get; init; } = QueueRetryPolicy.Exponential;

    /// <summary>
    /// The base delay between retries.
    /// For <see cref="QueueRetryPolicy.Fixed"/>, this is the constant delay.
    /// For <see cref="QueueRetryPolicy.Exponential"/>, this is the initial delay that doubles on each retry.
    /// Default is 5 seconds.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// When true, job progress and state are tracked via <see cref="IQueueJobStateStore"/>.
    /// Enables progress reporting, cancellation, and dashboard visibility.
    /// </summary>
    public bool TrackProgress { get; init; }

    /// <summary>
    /// The interval at which the worker polls the state store for cancellation requests.
    /// Only used when <see cref="TrackProgress"/> is true. Default is 5 seconds.
    /// </summary>
    public TimeSpan CancellationPollInterval { get; init; } = TimeSpan.FromSeconds(5);
}
