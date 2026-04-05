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
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, long>> _counterBuckets = new();
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

    public Task UpdateJobStatusAsync(string jobId, QueueJobStatus status, DateTimeOffset? startedUtc = null, DateTimeOffset? completedUtc = null, string? errorMessage = null, int? progress = null, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId, out var entry) || IsExpired(entry))
            return Task.CompletedTask;

        var now = _timeProvider.GetUtcNow();
        var expiresAt = expiry.HasValue ? now + expiry.Value : entry.ExpiresAt;
        var updated = entry.State with
        {
            Status = status,
            StartedUtc = startedUtc ?? entry.State.StartedUtc,
            CompletedUtc = completedUtc ?? entry.State.CompletedUtc,
            ErrorMessage = errorMessage ?? entry.State.ErrorMessage,
            Progress = progress ?? entry.State.Progress,
            LastUpdatedUtc = now
        };

        _jobs[jobId] = new JobEntry(updated, expiresAt);
        return Task.CompletedTask;
    }

    public Task UpdateJobProgressAsync(string jobId, int progress, string? progressMessage = null, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId, out var entry) || IsExpired(entry))
            return Task.CompletedTask;

        var now = _timeProvider.GetUtcNow();
        var expiresAt = expiry.HasValue ? now + expiry.Value : entry.ExpiresAt;
        var updated = entry.State with
        {
            Progress = progress,
            ProgressMessage = progressMessage,
            LastUpdatedUtc = now
        };

        _jobs[jobId] = new JobEntry(updated, expiresAt);
        return Task.CompletedTask;
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
        var bucketKey = GetBucketKey(queueName, _timeProvider.GetUtcNow());
        var bucket = _counterBuckets.GetOrAdd(bucketKey, _ => new ConcurrentDictionary<string, long>());
        bucket.AddOrUpdate(counterName, value, (_, existing) => existing + value);
        return Task.CompletedTask;
    }

    public Task<QueueCounterStats> GetCounterStatsAsync(string queueName, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var effectiveWindow = window ?? TimeSpan.FromHours(24);
        var startHour = TruncateToHour(now - effectiveWindow);
        var endHour = TruncateToHour(now);

        var totals = new Dictionary<string, long>();
        var buckets = new List<CounterBucket>();

        for (var hour = startHour; hour <= endHour; hour = hour.AddHours(1))
        {
            var bucketKey = GetBucketKey(queueName, hour);
            var counters = new Dictionary<string, long>();

            if (_counterBuckets.TryGetValue(bucketKey, out var bucket))
            {
                foreach (var kvp in bucket)
                {
                    counters[kvp.Key] = kvp.Value;
                    totals[kvp.Key] = totals.GetValueOrDefault(kvp.Key) + kvp.Value;
                }
            }

            buckets.Add(new CounterBucket { Hour = hour, Counters = counters });
        }

        return Task.FromResult(new QueueCounterStats { Totals = totals, Buckets = buckets });
    }

    private static string GetBucketKey(string queueName, DateTimeOffset timestamp)
    {
        var hour = TruncateToHour(timestamp);
        return $"{queueName}:{hour:yyyy-MM-ddTHH}";
    }

    private static DateTimeOffset TruncateToHour(DateTimeOffset timestamp)
        => new(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, 0, 0, TimeSpan.Zero);

    public Task<IReadOnlyList<QueueJobState>> GetJobsByStatusAsync(string queueName, QueueJobStatus status, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var results = _jobs.Values
            .Where(e => !IsExpired(e, now)
                && string.Equals(e.State.QueueName, queueName, StringComparison.OrdinalIgnoreCase)
                && e.State.Status == status)
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
