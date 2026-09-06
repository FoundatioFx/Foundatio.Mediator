namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Stores the state of tracked queue jobs. Implementations must be safe for concurrent use.
/// </summary>
public interface IQueueJobStateStore
{
    /// <summary>Whether jobs and cancellation requests are shared across processes. Decorators must forward this capability.</summary>
    bool IsShared => true;

    /// <summary>
    /// Creates or replaces a job's state. Called once at enqueue time with <see cref="QueueJobStatus.Queued"/>.
    /// </summary>
    Task SetJobStateAsync(QueueJobState state, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    Task<QueueJobState?> GetJobStateAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a job's status and optional fields. Implementations should apply the change atomically
    /// relative to other status updates for the same job. Returns false for a missing job, a terminal
    /// job, an older attempt, or a repeated start of an attempt already waiting for retry.
    /// Use SetJobStateAsync explicitly to reset a terminal job for administrative replay.
    /// </summary>
    Task<bool> UpdateJobStatusAsync(string jobId, QueueJobStatus status, DateTimeOffset? startedUtc = null, DateTimeOffset? completedUtc = null, string? errorMessage = null, int? progress = null, int? attempt = null, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    Task UpdateJobProgressAsync(string jobId, int progress, string? progressMessage = null, TimeSpan? expiry = null, CancellationToken cancellationToken = default, int? expectedAttempt = null)
        => Task.CompletedTask;

    /// <summary>
    /// Signals that the job is still alive. Called on every visibility renewal and progress report
    /// so stores can detect stalled jobs. Default is a no-op.
    /// </summary>
    Task HeartbeatAsync(string jobId, CancellationToken cancellationToken = default, int? expectedAttempt = null, TimeSpan? expiry = null)
        => Task.CompletedTask;

    Task<bool> RequestCancellationAsync(string jobId, CancellationToken cancellationToken = default);

    Task<bool> IsCancellationRequestedAsync(string jobId, CancellationToken cancellationToken = default);

    Task RemoveJobStateAsync(string jobId, CancellationToken cancellationToken = default);

    Task IncrementCounterAsync(string queueName, string counterName, long value = 1, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task<QueueCounterStats> GetCounterStatsAsync(string queueName, TimeSpan? window = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new QueueCounterStats
        {
            Totals = new Dictionary<string, long>(),
            Buckets = []
        });

    Task<IReadOnlyList<QueueJobState>> GetJobsByStatusAsync(string queueName, QueueJobStatus status, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<QueueJobState>>([]);

    Task<long> GetJobCountByStatusAsync(string queueName, QueueJobStatus status, CancellationToken cancellationToken = default)
        => Task.FromResult(0L);
}
