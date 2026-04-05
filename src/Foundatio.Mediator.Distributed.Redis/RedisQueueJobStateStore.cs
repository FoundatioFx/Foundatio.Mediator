using System.Globalization;
using StackExchange.Redis;

namespace Foundatio.Mediator.Distributed.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IQueueJobStateStore"/>.
/// Each job is stored as a Redis Hash. Per-queue job lists are maintained as sorted sets
/// scored by creation time for efficient pagination. Cancellation uses a separate key.
/// </summary>
public sealed class RedisQueueJobStateStore : IQueueJobStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisJobStateStoreOptions _options;

    public RedisQueueJobStateStore(IConnectionMultiplexer redis, RedisJobStateStoreOptions? options = null)
    {
        _redis = redis;
        _options = options ?? new RedisJobStateStoreOptions();
    }

    public async Task SetJobStateAsync(QueueJobState state, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = JobKey(state.JobId);
        var score = state.CreatedUtc.ToUnixTimeMilliseconds();

        // Read old status before overwriting (for status set migration)
        var oldStatusValue = await db.HashGetAsync(key, "Status").ConfigureAwait(false);

        var entries = new HashEntry[]
        {
            new("JobId", state.JobId),
            new("QueueName", state.QueueName),
            new("MessageType", state.MessageType),
            new("Status", ((int)state.Status).ToString(CultureInfo.InvariantCulture)),
            new("Progress", state.Progress.ToString(CultureInfo.InvariantCulture)),
            new("ProgressMessage", state.ProgressMessage ?? string.Empty),
            new("CreatedUtc", score.ToString(CultureInfo.InvariantCulture)),
            new("StartedUtc", state.StartedUtc?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            new("CompletedUtc", state.CompletedUtc?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            new("ErrorMessage", state.ErrorMessage ?? string.Empty),
            new("LastUpdatedUtc", state.LastUpdatedUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture))
        };

        await db.HashSetAsync(key, entries).ConfigureAwait(false);

        // Add to the per-queue sorted set (scored by creation timestamp for ordering)
        var queueSetKey = QueueSetKey(state.QueueName);
        await db.SortedSetAddAsync(queueSetKey, state.JobId, score).ConfigureAwait(false);

        // Maintain per-status sorted sets
        // Remove from old status set if status changed
        if (!oldStatusValue.IsNullOrEmpty && int.TryParse(oldStatusValue.ToString(), out var oldStatusInt))
        {
            var oldStatus = (QueueJobStatus)oldStatusInt;
            if (oldStatus != state.Status)
                await db.SortedSetRemoveAsync(StatusSetKey(state.QueueName, oldStatus), state.JobId).ConfigureAwait(false);
        }

        // Add to current status set
        await db.SortedSetAddAsync(StatusSetKey(state.QueueName, state.Status), state.JobId, score).ConfigureAwait(false);

        // Set TTL on all keys
        var ttl = expiry ?? _options.DefaultExpiry;
        if (ttl.HasValue)
        {
            await db.KeyExpireAsync(key, ttl.Value).ConfigureAwait(false);
            await db.KeyExpireAsync(queueSetKey, ttl.Value).ConfigureAwait(false);
            await db.KeyExpireAsync(StatusSetKey(state.QueueName, state.Status), ttl.Value).ConfigureAwait(false);
        }
    }

    public async Task<QueueJobState?> GetJobStateAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync(JobKey(jobId)).ConfigureAwait(false);

        if (entries.Length == 0)
            return null;

        return ParseJobState(entries);
    }

    public async Task UpdateJobStatusAsync(string jobId, QueueJobStatus status, DateTimeOffset? startedUtc = null, DateTimeOffset? completedUtc = null, string? errorMessage = null, int? progress = null, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = JobKey(jobId);

        // Read old status + queue name for sorted set migration
        var fields = await db.HashGetAsync(key, ["Status", "QueueName", "CreatedUtc"]).ConfigureAwait(false);
        if (fields[0].IsNullOrEmpty)
            return; // Job doesn't exist

        var queueName = fields[1].ToString();
        var createdScore = long.TryParse(fields[2].ToString(), out var cs) ? cs : 0d;
        var oldStatusInt = int.TryParse(fields[0].ToString(), out var osi) ? osi : -1;
        var oldStatus = (QueueJobStatus)oldStatusInt;
        var now = DateTimeOffset.UtcNow;

        // Build only the fields that need updating
        var updates = new List<HashEntry>
        {
            new("Status", ((int)status).ToString(CultureInfo.InvariantCulture)),
            new("LastUpdatedUtc", now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture))
        };

        if (startedUtc.HasValue)
            updates.Add(new HashEntry("StartedUtc", startedUtc.Value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));
        if (completedUtc.HasValue)
            updates.Add(new HashEntry("CompletedUtc", completedUtc.Value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)));
        if (errorMessage is not null)
            updates.Add(new HashEntry("ErrorMessage", errorMessage));
        if (progress.HasValue)
            updates.Add(new HashEntry("Progress", progress.Value.ToString(CultureInfo.InvariantCulture)));

        await db.HashSetAsync(key, updates.ToArray()).ConfigureAwait(false);

        // Migrate sorted sets if status changed
        if (oldStatus != status)
        {
            await db.SortedSetRemoveAsync(StatusSetKey(queueName, oldStatus), jobId).ConfigureAwait(false);
            await db.SortedSetAddAsync(StatusSetKey(queueName, status), jobId, createdScore).ConfigureAwait(false);
        }

        // Refresh TTL
        var ttl = expiry ?? _options.DefaultExpiry;
        if (ttl.HasValue)
        {
            await db.KeyExpireAsync(key, ttl.Value).ConfigureAwait(false);
            await db.KeyExpireAsync(StatusSetKey(queueName, status), ttl.Value).ConfigureAwait(false);
        }
    }

    public async Task UpdateJobProgressAsync(string jobId, int progress, string? progressMessage = null, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = JobKey(jobId);

        if (!await db.KeyExistsAsync(key).ConfigureAwait(false))
            return;

        var now = DateTimeOffset.UtcNow;
        var updates = new HashEntry[]
        {
            new("Progress", progress.ToString(CultureInfo.InvariantCulture)),
            new("ProgressMessage", progressMessage ?? string.Empty),
            new("LastUpdatedUtc", now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture))
        };

        await db.HashSetAsync(key, updates).ConfigureAwait(false);

        var ttl = expiry ?? _options.DefaultExpiry;
        if (ttl.HasValue)
            await db.KeyExpireAsync(key, ttl.Value).ConfigureAwait(false);
    }

    public async Task<bool> RequestCancellationAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = JobKey(jobId);

        // Check if job exists and is in a non-terminal state
        var statusValue = await db.HashGetAsync(key, "Status").ConfigureAwait(false);
        if (statusValue.IsNullOrEmpty)
            return false;

        if (int.TryParse(statusValue.ToString(), out var statusInt))
        {
            var status = (QueueJobStatus)statusInt;
            if (status is QueueJobStatus.Completed or QueueJobStatus.Failed or QueueJobStatus.Cancelled)
                return false;
        }

        // Set cancellation flag
        var cancelKey = CancelKey(jobId);
        await db.StringSetAsync(cancelKey, "1").ConfigureAwait(false);

        // Match the job's TTL
        var jobTtl = await db.KeyTimeToLiveAsync(key).ConfigureAwait(false);
        if (jobTtl.HasValue)
            await db.KeyExpireAsync(cancelKey, jobTtl.Value).ConfigureAwait(false);

        return true;
    }

    public Task<bool> IsCancellationRequestedAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        return db.KeyExistsAsync(CancelKey(jobId));
    }

    public async Task RemoveJobStateAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = JobKey(jobId);

        // Read queue name and status before deleting so we can clean up sorted sets
        var fields = await db.HashGetAsync(key, ["QueueName", "Status"]).ConfigureAwait(false);
        var queueName = fields[0];
        var statusValue = fields[1];

        await db.KeyDeleteAsync(key).ConfigureAwait(false);
        await db.KeyDeleteAsync(CancelKey(jobId)).ConfigureAwait(false);

        if (!queueName.IsNullOrEmpty)
        {
            var qn = queueName.ToString();
            await db.SortedSetRemoveAsync(QueueSetKey(qn), jobId).ConfigureAwait(false);

            // Remove from per-status sorted set
            if (!statusValue.IsNullOrEmpty && int.TryParse(statusValue.ToString(), out var statusInt))
                await db.SortedSetRemoveAsync(StatusSetKey(qn, (QueueJobStatus)statusInt), jobId).ConfigureAwait(false);
        }
    }

    public Task IncrementCounterAsync(string queueName, string counterName, long value = 1, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var bucketKey = CounterBucketKey(queueName, DateTimeOffset.UtcNow);
        var task = db.HashIncrementAsync(bucketKey, counterName, value);

        // Auto-expire each hourly bucket after 48h so old buckets clean themselves up
        _ = db.KeyExpireAsync(bucketKey, TimeSpan.FromHours(48), ExpireWhen.HasNoExpiry);

        return task;
    }

    public async Task<QueueCounterStats> GetCounterStatsAsync(string queueName, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var now = DateTimeOffset.UtcNow;
        var effectiveWindow = window ?? TimeSpan.FromHours(24);
        var startHour = TruncateToHour(now - effectiveWindow);
        var endHour = TruncateToHour(now);

        // Build list of bucket keys to query
        var hours = new List<DateTimeOffset>();
        for (var hour = startHour; hour <= endHour; hour = hour.AddHours(1))
            hours.Add(hour);

        // Pipeline all bucket reads in a single round-trip
        var batch = db.CreateBatch();
        var tasks = new Task<HashEntry[]>[hours.Count];
        for (int i = 0; i < hours.Count; i++)
            tasks[i] = batch.HashGetAllAsync(CounterBucketKey(queueName, hours[i]));
        batch.Execute();

        var totals = new Dictionary<string, long>();
        var buckets = new List<CounterBucket>(hours.Count);

        for (int i = 0; i < hours.Count; i++)
        {
            var entries = await tasks[i].ConfigureAwait(false);
            var counters = new Dictionary<string, long>(entries.Length);

            foreach (var entry in entries)
            {
                if (entry.Value.TryParse(out long val))
                {
                    var name = entry.Name.ToString();
                    counters[name] = val;
                    totals[name] = totals.GetValueOrDefault(name) + val;
                }
            }

            buckets.Add(new CounterBucket { Hour = hours[i], Counters = counters });
        }

        return new QueueCounterStats { Totals = totals, Buckets = buckets };
    }

    private string JobKey(string jobId) => $"{_options.KeyPrefix}:{jobId}";
    private string CancelKey(string jobId) => $"{_options.KeyPrefix}:{jobId}:cancel";
    private string QueueSetKey(string queueName) => $"{_options.KeyPrefix}:queues:{queueName}";
    private string StatusSetKey(string queueName, QueueJobStatus status) => $"{_options.KeyPrefix}:queues:{queueName}:status:{(int)status}";
    private string CounterBucketKey(string queueName, DateTimeOffset timestamp) => $"{_options.KeyPrefix}:counters:{queueName}:{TruncateToHour(timestamp):yyyy-MM-ddTHH}";

    private static DateTimeOffset TruncateToHour(DateTimeOffset timestamp)
        => new(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, 0, 0, TimeSpan.Zero);

    public async Task<IReadOnlyList<QueueJobState>> GetJobsByStatusAsync(string queueName, QueueJobStatus status, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var setKey = StatusSetKey(queueName, status);

        // O(take) — read only the page we need from the sorted set (newest first)
        var members = await db.SortedSetRangeByRankAsync(setKey, skip, skip + take - 1, Order.Descending).ConfigureAwait(false);

        if (members.Length == 0)
            return [];

        // Pipeline all hash reads
        var batch = db.CreateBatch();
        var tasks = new Task<HashEntry[]>[members.Length];
        for (int i = 0; i < members.Length; i++)
            tasks[i] = batch.HashGetAllAsync(JobKey(members[i].ToString()));
        batch.Execute();

        var results = new List<QueueJobState>(members.Length);
        for (int i = 0; i < tasks.Length; i++)
        {
            var entries = await tasks[i].ConfigureAwait(false);
            if (entries.Length > 0)
                results.Add(ParseJobState(entries));
        }

        return results;
    }

    public Task<long> GetJobCountByStatusAsync(string queueName, QueueJobStatus status, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        return db.SortedSetLengthAsync(StatusSetKey(queueName, status));
    }

    private static QueueJobState ParseJobState(HashEntry[] entries)
    {
        var dict = entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());

        return new QueueJobState
        {
            JobId = dict.GetValueOrDefault("JobId") ?? string.Empty,
            QueueName = dict.GetValueOrDefault("QueueName") ?? string.Empty,
            MessageType = dict.GetValueOrDefault("MessageType") ?? string.Empty,
            Status = int.TryParse(dict.GetValueOrDefault("Status"), out var s) ? (QueueJobStatus)s : QueueJobStatus.Queued,
            Progress = int.TryParse(dict.GetValueOrDefault("Progress"), out var p) ? p : 0,
            ProgressMessage = NullIfEmpty(dict.GetValueOrDefault("ProgressMessage")),
            CreatedUtc = ParseDateTimeOffset(dict.GetValueOrDefault("CreatedUtc")),
            StartedUtc = ParseNullableDateTimeOffset(dict.GetValueOrDefault("StartedUtc")),
            CompletedUtc = ParseNullableDateTimeOffset(dict.GetValueOrDefault("CompletedUtc")),
            ErrorMessage = NullIfEmpty(dict.GetValueOrDefault("ErrorMessage")),
            LastUpdatedUtc = ParseDateTimeOffset(dict.GetValueOrDefault("LastUpdatedUtc"))
        };
    }

    private static DateTimeOffset ParseDateTimeOffset(string? value)
        => long.TryParse(value, out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.MinValue;

    private static DateTimeOffset? ParseNullableDateTimeOffset(string? value)
        => string.IsNullOrEmpty(value) ? null : long.TryParse(value, out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrEmpty(value) ? null : value;
}
