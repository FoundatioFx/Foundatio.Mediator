namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Describes a registered queue worker and its runtime statistics.
/// Exposed via <see cref="IQueueWorkerRegistry"/> for dashboard and monitoring.
/// </summary>
public sealed class QueueWorkerInfo
{
    /// <summary>
    /// The name of the queue this worker processes.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>
    /// The full name of the message type.
    /// </summary>
    public required string MessageTypeName { get; init; }

    /// <summary>
    /// Number of concurrent consumer tasks.
    /// </summary>
    public int Concurrency { get; init; }

    /// <summary>
    /// Number of messages fetched per receive batch.
    /// </summary>
    public int PrefetchCount { get; init; }

    /// <summary>
    /// Maximum retry attempts before dead-lettering.
    /// </summary>
    public int MaxRetries { get; init; }

    /// <summary>
    /// Message visibility timeout.
    /// </summary>
    public TimeSpan VisibilityTimeout { get; init; }

    /// <summary>
    /// Queue group for selective hosting.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// The retry delay strategy.
    /// </summary>
    public QueueRetryPolicy RetryPolicy { get; init; }

    /// <summary>
    /// Whether job progress tracking is enabled.
    /// </summary>
    public bool TrackProgress { get; init; }

    // --- Runtime stats (updated atomically by QueueWorker) ---

    internal long _messagesProcessed;
    internal long _messagesFailed;
    internal long _messagesDeadLettered;
    internal volatile bool _isRunning;

    /// <summary>
    /// Total messages processed successfully since startup.
    /// </summary>
    public long MessagesProcessed => Interlocked.Read(ref _messagesProcessed);

    /// <summary>
    /// Total messages that failed processing since startup.
    /// </summary>
    public long MessagesFailed => Interlocked.Read(ref _messagesFailed);

    /// <summary>
    /// Total messages dead-lettered since startup.
    /// </summary>
    public long MessagesDeadLettered => Interlocked.Read(ref _messagesDeadLettered);

    /// <summary>
    /// Whether the worker is currently running.
    /// </summary>
    public bool IsRunning => _isRunning;
}
