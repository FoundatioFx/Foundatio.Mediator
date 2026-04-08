namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Thread-safe runtime statistics for a queue worker, updated atomically during message processing.
/// Exposed via <see cref="QueueWorkerInfo.Stats"/> for dashboard and monitoring.
/// </summary>
public sealed class QueueWorkerStats
{
    private long _messagesProcessed;
    private long _messagesFailed;
    private long _messagesDeadLettered;
    private volatile bool _isRunning;
    private volatile bool _workerRegistered;

    /// <summary>
    /// Whether a <see cref="QueueWorker"/> hosted service was registered for this queue
    /// in the current process. When <c>false</c>, the worker metadata is available for
    /// dashboard visibility but no local processing occurs (e.g., API-only nodes).
    /// </summary>
    public bool WorkerRegistered => _workerRegistered;

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
    internal void SetWorkerRegistered(bool registered) => _workerRegistered = registered;
}
