using System.Collections.Concurrent;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// In-memory implementation of <see cref="IQueueJobStateStore"/>.
/// Suitable for development, testing, and single-node deployments.
/// Expired entries are lazily cleaned up on access.
/// </summary>
public sealed class InMemoryQueueJobStateStore : IQueueJobStateStore
{
    private readonly ConcurrentDictionary<string, JobEntry> _jobs = new();
    private readonly ConcurrentDictionary<string, bool> _cancellations = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, long>> _counters = new();
    private readonly TimeProvider _timeProvider;
    private int _accessCount;

    public InMemoryQueueJobStateStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task SetJobStateAsync(QueueJobState state, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresAt = expiry.HasValue ? now + expiry.Value : DateTimeOffset.MaxValue;

        _jobs[state.JobId] = new JobEntry(state, expiresAt);

        CleanupIfNeeded();

        return Task.CompletedTask;
    }

    public Task<QueueJobState?> GetJobStateAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var entry) && !IsExpired(entry))
            return Task.FromResult<QueueJobState?>(entry.State);

        // Remove expired entry on access
        if (entry is not null)
        {
            _jobs.TryRemove(jobId, out _);
            _cancellations.TryRemove(jobId, out _);
        }

        return Task.FromResult<QueueJobState?>(null);
    }

    public Task<IReadOnlyList<QueueJobState>> GetJobsByQueueAsync(string queueName, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var results = _jobs.Values
            .Where(e => !IsExpired(e, now) && string.Equals(e.State.QueueName, queueName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.State.CreatedUtc)
            .Skip(skip)
            .Take(take)
            .Select(e => e.State)
            .ToList();

        return Task.FromResult<IReadOnlyList<QueueJobState>>(results);
    }

    public Task<bool> RequestCancellationAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId, out var entry) || IsExpired(entry))
            return Task.FromResult(false);

        // Only allow cancellation for non-terminal states
        if (entry.State.Status is QueueJobStatus.Completed or QueueJobStatus.Failed or QueueJobStatus.Cancelled)
            return Task.FromResult(false);

        _cancellations[jobId] = true;
        return Task.FromResult(true);
    }

    public Task<bool> IsCancellationRequestedAsync(string jobId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_cancellations.ContainsKey(jobId));
    }

    public Task RemoveJobStateAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _jobs.TryRemove(jobId, out _);
        _cancellations.TryRemove(jobId, out _);
        return Task.CompletedTask;
    }

    public Task IncrementCounterAsync(string queueName, string counterName, long value = 1, CancellationToken cancellationToken = default)
    {
        var queueCounters = _counters.GetOrAdd(queueName, _ => new ConcurrentDictionary<string, long>());
        queueCounters.AddOrUpdate(counterName, value, (_, existing) => existing + value);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, long>> GetCountersAsync(string queueName, CancellationToken cancellationToken = default)
    {
        if (_counters.TryGetValue(queueName, out var queueCounters))
            return Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>(queueCounters));

        return Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>());
    }

    public Task<IReadOnlyList<QueueJobState>> GetJobsByStatusAsync(string queueName, QueueJobStatus[] statuses, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var statusSet = new HashSet<QueueJobStatus>(statuses);
        var results = _jobs.Values
            .Where(e => !IsExpired(e, now)
                && string.Equals(e.State.QueueName, queueName, StringComparison.OrdinalIgnoreCase)
                && statusSet.Contains(e.State.Status))
            .OrderByDescending(e => e.State.CreatedUtc)
            .Skip(skip)
            .Take(take)
            .Select(e => e.State)
            .ToList();

        return Task.FromResult<IReadOnlyList<QueueJobState>>(results);
    }

    public Task<long> GetJobCountByStatusAsync(string queueName, QueueJobStatus status, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var count = _jobs.Values.Count(e => !IsExpired(e, now)
            && string.Equals(e.State.QueueName, queueName, StringComparison.OrdinalIgnoreCase)
            && e.State.Status == status);

        return Task.FromResult((long)count);
    }

    private bool IsExpired(JobEntry entry) => IsExpired(entry, _timeProvider.GetUtcNow());

    private static bool IsExpired(JobEntry entry, DateTimeOffset now) => now >= entry.ExpiresAt;

    private void CleanupIfNeeded()
    {
        // Run cleanup every 100 writes to avoid accumulating expired entries
        if (Interlocked.Increment(ref _accessCount) % 100 != 0)
            return;

        var now = _timeProvider.GetUtcNow();
        foreach (var kvp in _jobs)
        {
            if (now >= kvp.Value.ExpiresAt)
            {
                _jobs.TryRemove(kvp.Key, out _);
                _cancellations.TryRemove(kvp.Key, out _);
            }
        }
    }

    private sealed record JobEntry(QueueJobState State, DateTimeOffset ExpiresAt);
}
