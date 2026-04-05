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
    /// Implementations must return an independent copy so callers can mutate
    /// properties without affecting stored data.
    /// </summary>
    /// <returns>The job state, or <c>null</c> if the job ID is not found or has expired.</returns>
    Task<QueueJobState?> GetJobStateAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves tracked jobs for a specific queue, ordered by creation time descending.
    /// </summary>
    Task<IReadOnlyList<QueueJobState>> GetJobsByQueueAsync(string queueName, int skip = 0, int take = 50, CancellationToken cancellationToken = default);

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
    /// Atomically increments a named counter for a queue.
    /// Used to track messages processed, failed, and dead-lettered across all nodes.
    /// </summary>
    /// <param name="queueName">The queue name.</param>
    /// <param name="counterName">Counter name (e.g., "processed", "failed", "dead_lettered").</param>
    /// <param name="value">The amount to increment by. Default is 1.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IncrementCounterAsync(string queueName, string counterName, long value = 1, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Retrieves all counters for a queue (e.g., processed, failed, dead_lettered).
    /// </summary>
    /// <param name="queueName">The queue name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary of counter name to value. Empty if no counters exist.</returns>
    Task<IReadOnlyDictionary<string, long>> GetCountersAsync(string queueName, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>());

    /// <summary>
    /// Retrieves tracked jobs filtered by one or more statuses, ordered by creation time descending.
    /// </summary>
    async Task<IReadOnlyList<QueueJobState>> GetJobsByStatusAsync(string queueName, QueueJobStatus[] statuses, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        // Default: fall back to GetJobsByQueueAsync and filter in memory
        var all = await GetJobsByQueueAsync(queueName, 0, skip + take + 500, cancellationToken).ConfigureAwait(false);
        var statusSet = new HashSet<QueueJobStatus>(statuses);
        return all
            .Where(j => statusSet.Contains(j.Status))
            .Skip(skip)
            .Take(take)
            .ToList();
    }

    /// <summary>
    /// Counts jobs in a specific status for a queue.
    /// </summary>
    async Task<long> GetJobCountByStatusAsync(string queueName, QueueJobStatus status, CancellationToken cancellationToken = default)
    {
        var all = await GetJobsByQueueAsync(queueName, 0, int.MaxValue, cancellationToken).ConfigureAwait(false);
        return all.Count(j => j.Status == status);
    }
}
