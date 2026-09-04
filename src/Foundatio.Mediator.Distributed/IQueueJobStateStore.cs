namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Pluggable store for tracking queue job state, progress, and cancellation.
/// Implementations must be thread-safe.
/// </summary>
public interface IQueueJobStateStore
{
    /// <summary>
    /// Persists or updates a job state entry. When <paramref name="expiry"/> is provided,
    /// the entry should be automatically removed after the specified duration.
    /// </summary>
    Task SetJobStateAsync(QueueJobState state, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the state for a specific job.
    /// </summary>
    /// <returns>The job state, or <c>null</c> if the job ID is not found or has expired.</returns>
    Task<QueueJobState?> GetJobStateAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically updates job status and related fields without requiring a prior read.
    /// Implementations should update only the supplied fields plus <c>LastUpdatedUtc</c>.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="status">The new status.</param>
    /// <param name="startedUtc">When processing started (set when transitioning to <see cref="QueueJobStatus.Processing"/>).</param>
    /// <param name="completedUtc">When the job reached a terminal state.</param>
    /// <param name="errorMessage">Error details (set when transitioning to <see cref="QueueJobStatus.Failed"/>).</param>
    /// <param name="progress">Progress percentage to set alongside the status change.</param>
    /// <param name="attempt">The processing attempt number (1 = first try, 2+ = retry).</param>
    /// <param name="expiry">Optional sliding expiry for the entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateJobStatusAsync(string jobId, QueueJobStatus status, DateTimeOffset? startedUtc = null, DateTimeOffset? completedUtc = null, string? errorMessage = null, int? progress = null, int? attempt = null, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically updates job progress and optional message without requiring a prior read.
    /// Implementations should also update <c>LastUpdatedUtc</c>.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="progress">Progress percentage (0–100).</param>
    /// <param name="progressMessage">Optional description of current work.</param>
    /// <param name="expiry">Optional sliding expiry for the entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateJobProgressAsync(string jobId, int progress, string? progressMessage = null, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Requests cancellation of a job. The worker will observe this on the next
    /// cancellation poll or progress report and cancel the handler's <see cref="CancellationToken"/>.
    /// </summary>
    /// <returns><c>true</c> if the job was found and cancellation was requested; <c>false</c> otherwise.</returns>
    Task<bool> RequestCancellationAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether cancellation has been requested for a job.
    /// Called by the worker's cancellation polling loop and on progress reports.
    /// </summary>
    Task<bool> IsCancellationRequestedAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a job state entry.
    /// </summary>
    Task RemoveJobStateAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments a named counter for a queue within the current hourly bucket.
    /// Used to track messages processed, failed, and dead-lettered across all nodes.
    /// Implementations should bucket by UTC hour for time-windowed queries.
    /// </summary>
    /// <param name="queueName">The queue name.</param>
    /// <param name="counterName">Counter name (e.g., "processed", "failed", "dead_lettered").</param>
    /// <param name="value">The amount to increment by. Default is 1.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IncrementCounterAsync(string queueName, string counterName, long value = 1, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Retrieves counter statistics for a queue over a time window, including per-hour buckets
    /// for sparkline rendering and aggregated totals.
    /// </summary>
    /// <param name="queueName">The queue name.</param>
    /// <param name="window">The time window to query. Defaults to 24 hours.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Counter totals and per-hour buckets ordered oldest to newest.</returns>
    Task<QueueCounterStats> GetCounterStatsAsync(string queueName, TimeSpan? window = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new QueueCounterStats
        {
            Totals = new Dictionary<string, long>(),
            Buckets = []
        });

    /// <summary>
    /// Retrieves tracked jobs for a given status, ordered by creation time descending.
    /// </summary>
    Task<IReadOnlyList<QueueJobState>> GetJobsByStatusAsync(string queueName, QueueJobStatus status, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<QueueJobState>>([]);

    /// <summary>
    /// Counts jobs in a specific status for a queue.
    /// </summary>
    Task<long> GetJobCountByStatusAsync(string queueName, QueueJobStatus status, CancellationToken cancellationToken = default)
        => Task.FromResult(0L);
}
