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
    /// Maximum number of attempts before dead-lettering.
    /// </summary>
    public int MaxAttempts { get; init; }

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

    /// <summary>
    /// A human-readable description of the queue.
    /// </summary>
    public string? Description { get; init; }

    // --- Runtime stats (updated atomically by QueueWorker) ---

    private long _messagesProcessed;
    private long _messagesFailed;
    private long _messagesDeadLettered;
    private volatile bool _isRunning;

    /// <summary>
    /// Whether a <see cref="QueueWorker"/> hosted service was registered for this queue
    /// in the current process. When <c>false</c>, the worker metadata is available for
    /// dashboard visibility but no local processing occurs (e.g., API-only nodes).
    /// </summary>
    public bool WorkerRegistered { get; internal set; }

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

    internal void IncrementProcessed() => Interlocked.Increment(ref _messagesProcessed);
    internal void IncrementFailed() => Interlocked.Increment(ref _messagesFailed);
    internal void IncrementDeadLettered() => Interlocked.Increment(ref _messagesDeadLettered);
    internal void SetRunning(bool running) => _isRunning = running;
}
