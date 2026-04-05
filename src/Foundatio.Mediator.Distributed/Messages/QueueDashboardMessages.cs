namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Gets a list of all registered queue workers with their configuration and runtime stats.
/// </summary>
public record GetQueueWorkers;

/// <summary>
/// Gets a specific queue worker by queue name, including queue stats.
/// </summary>
public record GetQueueWorker(string QueueName);

/// <summary>
/// Gets a dashboard view of jobs for a queue: queued count, active jobs, and recent terminal jobs.
/// </summary>
public record GetQueueJobDashboard(string QueueName, int? RecentTerminalCount = 20);

/// <summary>
/// Gets the state of a specific tracked job.
/// </summary>
public record GetQueueJob(string JobId);

/// <summary>
/// Requests cancellation of a tracked job.
/// </summary>
public record CancelQueueJob(string JobId);

/// <summary>
/// Summary of a queue worker's configuration and runtime statistics.
/// </summary>
public record QueueWorkerSummary
{
    public required string QueueName { get; init; }
    public required string MessageTypeName { get; init; }
    public int Concurrency { get; init; }
    public int PrefetchCount { get; init; }
    public int MaxRetries { get; init; }
    public required string VisibilityTimeout { get; init; }
    public string? Group { get; init; }
    public required string RetryPolicy { get; init; }
    public bool TrackProgress { get; init; }
    public bool IsRunning { get; init; }
    public long MessagesProcessed { get; init; }
    public long MessagesFailed { get; init; }
    public long MessagesDeadLettered { get; init; }
    public QueueStats? Stats { get; init; }
}

/// <summary>
/// Result for a cancellation request.
/// </summary>
public record QueueJobCancellationResult(string JobId, bool CancellationRequested);

/// <summary>
/// Dashboard view of jobs for a queue, partitioned by lifecycle phase.
/// </summary>
public record QueueJobDashboardView
{
    /// <summary>Number of jobs waiting in the queue (not yet picked up).</summary>
    public long QueuedCount { get; init; }

    /// <summary>Jobs currently being processed.</summary>
    public required IReadOnlyList<QueueJobState> ActiveJobs { get; init; }

    /// <summary>Recently completed, failed, or cancelled jobs.</summary>
    public required IReadOnlyList<QueueJobState> RecentJobs { get; init; }

    /// <summary>
    /// Counter statistics with per-hour buckets for sparkline rendering.
    /// Includes totals and hourly breakdown of processed, failed, and dead-lettered counts.
    /// </summary>
    public QueueCounterStats? CounterStats { get; init; }
}
