using System.Globalization;
using StackExchange.Redis;

namespace Foundatio.Mediator.Distributed.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IQueueJobStateStore"/>.
/// Each job is stored as a Redis Hash. Per-queue and per-status sorted sets scored by creation time
/// index jobs for pagination. Cancellation uses a separate key that shares the job's TTL.
/// </summary>
/// <remarks>
/// Key layout (<c>{prefix}</c> is <see cref="RedisJobStateStoreOptions.KeyPrefix"/>, optionally preceded by
/// <see cref="RedisJobStateStoreOptions.ResourcePrefix"/>):
/// <list type="bullet">
/// <item><c>{prefix}:{jobId}</c> — hash of job fields; metadata is stored as <c>meta:{name}</c> fields</item>
/// <item><c>{prefix}:{jobId}:cancel</c> — cancellation flag</item>
/// <item><c>{prefix}:queues:{queueName}</c> — sorted set of every job in the queue</item>
/// <item><c>{prefix}:queues:{queueName}:status:{status}</c> — sorted set per <see cref="QueueJobStatus"/> value</item>
/// <item><c>{prefix}:counters:{queueName}:{yyyy-MM-ddTHH}</c> — hourly counter hash</item>
/// </list>
/// Every write to a job is a single conditional MULTI/EXEC transaction, so a job is a member of exactly one
/// status set at any time. Sorted-set members whose job hash has expired are trimmed by creation time on
/// write and removed when a listing finds them missing, so counts are approximate until a listing runs.
/// </remarks>
public sealed class RedisQueueJobStateStore : IQueueJobStateStore
{
    private const int MaxTransactionAttempts = 10;
    private const string MetadataFieldPrefix = "meta:";
    private static readonly TimeSpan CounterBucketRetention = TimeSpan.FromHours(48);

    private readonly IConnectionMultiplexer _redis;
    private readonly RedisJobStateStoreOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _keyPrefix;

    public RedisQueueJobStateStore(IConnectionMultiplexer redis, RedisJobStateStoreOptions? options = null, TimeProvider? timeProvider = null)
    {
        _redis = redis;
        _options = options ?? new RedisJobStateStoreOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _keyPrefix = string.IsNullOrEmpty(_options.ResourcePrefix)
            ? _options.KeyPrefix
            : $"{_options.ResourcePrefix}:{_options.KeyPrefix}";
    }

    public Task SetJobStateAsync(QueueJobState state, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = JobKey(state.JobId);
        var score = state.CreatedUtc.ToUnixTimeMilliseconds();
        var ttl = ResolveTtl(expiry, state.Status);
        var entries = BuildEntries(state, score);
        var queueSetKey = QueueSetKey(state.QueueName);
        var statusSetKey = StatusSetKey(state.QueueName, state.Status);

        return RunTransactionAsync(state.JobId, async () =>
        {
            var batch = db.CreateBatch();
            var statusTask = batch.HashGetAsync(key, "Status");
            var queueIndex = new IndexExpiryRead(batch, queueSetKey);
            var statusIndex = new IndexExpiryRead(batch, statusSetKey);
            batch.Execute();

            var oldStatusRaw = await statusTask.ConfigureAwait(false);
            var extendQueueIndex = await queueIndex.NeedsExtensionAsync(ttl).ConfigureAwait(false);
            var extendStatusIndex = await statusIndex.NeedsExtensionAsync(ttl).ConfigureAwait(false);

            var txn = db.CreateTransaction();
            txn.AddCondition(oldStatusRaw.IsNull
                ? Condition.HashNotExists(key, "Status")
                : Condition.HashEqual(key, "Status", oldStatusRaw));

            // Delete first so fields (including metadata) from a previous version of the job do not linger.
            _ = txn.KeyDeleteAsync(key);
            _ = txn.HashSetAsync(key, entries);

            if (TryParseStatus(oldStatusRaw, out var oldStatus) && oldStatus != state.Status)
                _ = txn.SortedSetRemoveAsync(StatusSetKey(state.QueueName, oldStatus), state.JobId);

            _ = txn.SortedSetAddAsync(queueSetKey, state.JobId, score);
            _ = txn.SortedSetAddAsync(statusSetKey, state.JobId, score);
            ApplyExpiry(txn, ttl, key, CancelKey(state.JobId), (queueSetKey, extendQueueIndex), (statusSetKey, extendStatusIndex));

            return await txn.ExecuteAsync().ConfigureAwait(false);
        }, cancellationToken);
    }

    public async Task<QueueJobState?> GetJobStateAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync(JobKey(jobId)).ConfigureAwait(false);

        if (entries.Length == 0)
            return null;

        return ParseJobState(entries);
    }

    public Task UpdateJobStatusAsync(string jobId, QueueJobStatus status, DateTimeOffset? startedUtc = null, DateTimeOffset? completedUtc = null, string? errorMessage = null, int? progress = null, int? attempt = null, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = JobKey(jobId);
        var ttl = ResolveTtl(expiry, status);

        return RunTransactionAsync(jobId, async () =>
        {
            var fields = await db.HashGetAsync(key, ["QueueName", "CreatedUtc", "Status"]).ConfigureAwait(false);
            if (fields[0].IsNull)
                return null;

            var queueName = fields[0].ToString();
            var createdScore = fields[1].TryParse(out long cs) ? cs : 0L;
            var oldStatusRaw = fields[2];

            var updates = new List<HashEntry>
            {
                new("Status", FormatStatus(status)),
                new("LastUpdatedUtc", FormatTimestamp(_timeProvider.GetUtcNow()))
            };

            if (startedUtc.HasValue)
                updates.Add(new("StartedUtc", FormatTimestamp(startedUtc.Value)));
            if (completedUtc.HasValue)
                updates.Add(new("CompletedUtc", FormatTimestamp(completedUtc.Value)));
            if (errorMessage is not null)
                updates.Add(new("ErrorMessage", errorMessage));
            if (progress.HasValue)
                updates.Add(new("Progress", FormatInt(progress.Value)));
            if (attempt.HasValue)
                updates.Add(new("Attempt", FormatInt(attempt.Value)));

            var newStatusSetKey = StatusSetKey(queueName, status);
            var extendStatusIndex = await IndexExpiryRead.NeedsExtensionAsync(db, newStatusSetKey, ttl).ConfigureAwait(false);

            var txn = db.CreateTransaction();
            txn.AddCondition(Condition.HashEqual(key, "Status", oldStatusRaw));

            _ = txn.HashSetAsync(key, updates.ToArray());

            if (TryParseStatus(oldStatusRaw, out var oldStatus) && oldStatus != status)
                _ = txn.SortedSetRemoveAsync(StatusSetKey(queueName, oldStatus), jobId);
            _ = txn.SortedSetAddAsync(newStatusSetKey, jobId, createdScore);
            ApplyExpiry(txn, ttl, key, CancelKey(jobId), (newStatusSetKey, extendStatusIndex));

            return await txn.ExecuteAsync().ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task UpdateJobProgressAsync(string jobId, int progress, string? progressMessage = null, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = JobKey(jobId);

        return RunTransactionAsync(jobId, async () =>
        {
            var statusRaw = await db.HashGetAsync(key, "Status").ConfigureAwait(false);
            if (statusRaw.IsNull)
                return null;

            var status = TryParseStatus(statusRaw, out var s) ? s : QueueJobStatus.Processing;
            var updates = new HashEntry[]
            {
                new("Progress", FormatInt(progress)),
                new("ProgressMessage", progressMessage ?? string.Empty),
                new("LastUpdatedUtc", FormatTimestamp(_timeProvider.GetUtcNow()))
            };

            var txn = db.CreateTransaction();
            txn.AddCondition(Condition.HashEqual(key, "Status", statusRaw));
            _ = txn.HashSetAsync(key, updates);
            ApplyExpiry(txn, ResolveTtl(expiry, status), key, CancelKey(jobId));

            return await txn.ExecuteAsync().ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task HeartbeatAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = JobKey(jobId);
        var now = FormatTimestamp(_timeProvider.GetUtcNow());

        var txn = db.CreateTransaction();
        txn.AddCondition(Condition.KeyExists(key));
        _ = txn.HashSetAsync(key, [new HashEntry("LastUpdatedUtc", now), new HashEntry("LastHeartbeatUtc", now)]);

        // A false result means the job no longer exists, which is not an error for a heartbeat.
        return txn.ExecuteAsync();
    }

    public async Task<bool> RequestCancellationAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = JobKey(jobId);
        var cancelKey = CancelKey(jobId);
        bool requested = false;

        await RunTransactionAsync(jobId, async () =>
        {
            var batch = db.CreateBatch();
            var statusTask = batch.HashGetAsync(key, "Status");
            var ttlTask = batch.KeyTimeToLiveAsync(key);
            batch.Execute();

            var statusRaw = await statusTask.ConfigureAwait(false);
            var jobTtl = await ttlTask.ConfigureAwait(false);

            if (statusRaw.IsNull || (TryParseStatus(statusRaw, out var status) && IsTerminal(status)))
                return null;

            var txn = db.CreateTransaction();
            txn.AddCondition(Condition.HashEqual(key, "Status", statusRaw));
            _ = txn.StringSetAsync(cancelKey, "1", jobTtl, keepTtl: false, When.Always, CommandFlags.None);

            requested = await txn.ExecuteAsync().ConfigureAwait(false);
            return requested;
        }, cancellationToken).ConfigureAwait(false);

        return requested;
    }

    public Task<bool> IsCancellationRequestedAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        return db.KeyExistsAsync(CancelKey(jobId));
    }

    public Task RemoveJobStateAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = JobKey(jobId);

        return RunTransactionAsync(jobId, async () =>
        {
            var fields = await db.HashGetAsync(key, ["QueueName", "Status"]).ConfigureAwait(false);

            var txn = db.CreateTransaction();
            _ = txn.KeyDeleteAsync(CancelKey(jobId));

            if (fields[0].IsNull)
            {
                txn.AddCondition(Condition.KeyNotExists(key));
            }
            else
            {
                var queueName = fields[0].ToString();
                txn.AddCondition(Condition.HashEqual(key, "Status", fields[1]));
                _ = txn.KeyDeleteAsync(key);
                _ = txn.SortedSetRemoveAsync(QueueSetKey(queueName), jobId);
                if (TryParseStatus(fields[1], out var status))
                    _ = txn.SortedSetRemoveAsync(StatusSetKey(queueName, status), jobId);
            }

            return await txn.ExecuteAsync().ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task IncrementCounterAsync(string queueName, string counterName, long value = 1, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var bucketKey = CounterBucketKey(queueName, _timeProvider.GetUtcNow());

        // Buckets only receive writes during their own hour, so refreshing the TTL on every increment
        // keeps them alive for at most retention + 1h. Unconditional EXPIRE works on Redis 6.
        var txn = db.CreateTransaction();
        _ = txn.HashIncrementAsync(bucketKey, counterName, value);
        _ = txn.KeyExpireAsync(bucketKey, CounterBucketRetention);

        return txn.ExecuteAsync();
    }

    public async Task<QueueCounterStats> GetCounterStatsAsync(string queueName, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var now = _timeProvider.GetUtcNow();
        var effectiveWindow = window ?? TimeSpan.FromHours(24);
        var startHour = TruncateToHour(now - effectiveWindow);
        var endHour = TruncateToHour(now);

        var hours = new List<DateTimeOffset>();
        for (var hour = startHour; hour <= endHour; hour = hour.AddHours(1))
            hours.Add(hour);

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

    public async Task<IReadOnlyList<QueueJobState>> GetJobsByStatusAsync(string queueName, QueueJobStatus status, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        if (take <= 0)
            return [];

        var db = _redis.GetDatabase();
        var setKey = StatusSetKey(queueName, status);
        var results = new List<QueueJobState>(take);
        var dangling = new List<RedisValue>();
        long cursor = Math.Max(skip, 0);

        // Keep paging past members whose hash has expired so the caller still gets a full page.
        while (results.Count < take)
        {
            int wanted = take - results.Count;
            var members = await db.SortedSetRangeByRankAsync(setKey, cursor, cursor + wanted - 1, Order.Descending).ConfigureAwait(false);
            if (members.Length == 0)
                break;

            cursor += members.Length;

            var batch = db.CreateBatch();
            var tasks = new Task<HashEntry[]>[members.Length];
            for (int i = 0; i < members.Length; i++)
                tasks[i] = batch.HashGetAllAsync(JobKey(members[i].ToString()));
            batch.Execute();

            for (int i = 0; i < tasks.Length; i++)
            {
                var entries = await tasks[i].ConfigureAwait(false);
                if (entries.Length > 0)
                    results.Add(ParseJobState(entries));
                else
                    dangling.Add(members[i]);
            }

            if (members.Length < wanted)
                break;
        }

        // Removal is deferred until after paging so ranks stay stable while reading.
        if (dangling.Count > 0)
            await db.SortedSetRemoveAsync(setKey, dangling.ToArray()).ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Returns the size of the status index after trimming members older than the expiry window.
    /// Members whose hash expired inside the window are still counted until a listing removes them.
    /// </summary>
    public async Task<long> GetJobCountByStatusAsync(string queueName, QueueJobStatus status, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var setKey = StatusSetKey(queueName, status);

        if (TryGetTrimCutoff(ResolveTtl(null, status), out var cutoff))
            await db.SortedSetRemoveRangeByScoreAsync(setKey, double.NegativeInfinity, cutoff).ConfigureAwait(false);

        return await db.SortedSetLengthAsync(setKey).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="attemptAsync"/> until it commits. The delegate returns <c>true</c> when its
    /// transaction committed, <c>false</c> when a condition failed and it should be retried against fresh
    /// state, or <c>null</c> when the job no longer exists and there is nothing to do.
    /// </summary>
    private static async Task RunTransactionAsync(string jobId, Func<Task<bool?>> attemptAsync, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var committed = await attemptAsync().ConfigureAwait(false);
            if (committed != false)
                return;

            if (attempt >= MaxTransactionAttempts)
                throw new InvalidOperationException($"Job state for '{jobId}' was modified concurrently on {attempt} consecutive attempts; giving up.");

            await Task.Delay(Random.Shared.Next(1, 8 * attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan? ResolveTtl(TimeSpan? expiry, QueueJobStatus status)
    {
        var ttl = expiry ?? _options.DefaultExpiry;
        if (ttl is null || IsTerminal(status))
            return ttl;

        return ttl.Value > _options.NonTerminalExpiry ? ttl : _options.NonTerminalExpiry;
    }

    /// <summary>
    /// Applies <paramref name="ttl"/> to the job and cancellation keys and trims each index. An index's own TTL
    /// is only ever extended (see <see cref="IndexExpiryRead"/>), so a write with a short expiry cannot make the
    /// index disappear while longer-lived members are still in it.
    /// </summary>
    private void ApplyExpiry(ITransaction txn, TimeSpan? ttl, RedisKey jobKey, RedisKey cancelKey, params (RedisKey Key, bool Extend)[] indexes)
    {
        if (ttl is null)
            return;

        _ = txn.KeyExpireAsync(jobKey, ttl);
        _ = txn.KeyExpireAsync(cancelKey, ttl);

        bool trim = TryGetTrimCutoff(ttl, out var cutoff);
        foreach (var (index, extend) in indexes)
        {
            if (extend)
                _ = txn.KeyExpireAsync(index, ttl);
            if (trim)
                _ = txn.SortedSetRemoveRangeByScoreAsync(index, double.NegativeInfinity, cutoff);
        }
    }

    /// <summary>
    /// Pipelined EXISTS + TTL of an index key, used to decide whether a write may set the index's expiry:
    /// yes when the index does not exist yet or its remaining TTL is shorter than the new one; never for an
    /// index that exists without a TTL. Redis 6 has no <c>EXPIRE GT</c>, so the comparison happens client-side.
    /// </summary>
    private sealed class IndexExpiryRead(IBatch batch, RedisKey key)
    {
        private readonly Task<bool> _exists = batch.KeyExistsAsync(key);
        private readonly Task<TimeSpan?> _ttl = batch.KeyTimeToLiveAsync(key);

        public async Task<bool> NeedsExtensionAsync(TimeSpan? ttl)
        {
            if (ttl is null)
                return false;

            if (!await _exists.ConfigureAwait(false))
                return true;

            var remaining = await _ttl.ConfigureAwait(false);
            return remaining is { } current && current < ttl.Value;
        }

        public static Task<bool> NeedsExtensionAsync(IDatabase db, RedisKey key, TimeSpan? ttl)
        {
            var batch = db.CreateBatch();
            var read = new IndexExpiryRead(batch, key);
            batch.Execute();
            return read.NeedsExtensionAsync(ttl);
        }
    }

    /// <summary>
    /// A member created earlier than <c>now - (ttl + NonTerminalExpiry)</c> cannot have a live hash: its state
    /// would have had to stay non-terminal longer than <see cref="RedisJobStateStoreOptions.NonTerminalExpiry"/>
    /// before its final write. Anything older is safe to drop from an index by score.
    /// </summary>
    private bool TryGetTrimCutoff(TimeSpan? ttl, out double cutoff)
    {
        cutoff = 0;
        if (ttl is null)
            return false;

        var now = _timeProvider.GetUtcNow();
        var window = ttl.Value + _options.NonTerminalExpiry;
        if (window >= now - DateTimeOffset.UnixEpoch)
            return false;

        cutoff = (now - window).ToUnixTimeMilliseconds();
        return true;
    }

    private static bool IsTerminal(QueueJobStatus status)
        => status is QueueJobStatus.Completed or QueueJobStatus.Failed or QueueJobStatus.Cancelled;

    private static bool TryParseStatus(RedisValue raw, out QueueJobStatus status)
    {
        if (raw.TryParse(out int value))
        {
            status = (QueueJobStatus)value;
            return true;
        }

        status = default;
        return false;
    }

    private static HashEntry[] BuildEntries(QueueJobState state, long createdScore)
    {
        var entries = new List<HashEntry>(12 + (state.Metadata?.Count ?? 0))
        {
            new("JobId", state.JobId),
            new("QueueName", state.QueueName),
            new("MessageType", state.MessageType),
            new("Status", FormatStatus(state.Status)),
            new("Progress", FormatInt(state.Progress)),
            new("ProgressMessage", state.ProgressMessage ?? string.Empty),
            new("CreatedUtc", createdScore.ToString(CultureInfo.InvariantCulture)),
            new("StartedUtc", state.StartedUtc is { } started ? FormatTimestamp(started) : string.Empty),
            new("CompletedUtc", state.CompletedUtc is { } completed ? FormatTimestamp(completed) : string.Empty),
            new("ErrorMessage", state.ErrorMessage ?? string.Empty),
            new("Attempt", FormatInt(state.Attempt)),
            new("LastUpdatedUtc", FormatTimestamp(state.LastUpdatedUtc))
        };

        if (state.Metadata is not null)
        {
            foreach (var kvp in state.Metadata)
                entries.Add(new(MetadataFieldPrefix + kvp.Key, kvp.Value));
        }

        return entries.ToArray();
    }

    private static string FormatStatus(QueueJobStatus status) => ((int)status).ToString(CultureInfo.InvariantCulture);
    private static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string FormatTimestamp(DateTimeOffset value) => value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    private string JobKey(string jobId) => $"{_keyPrefix}:{jobId}";
    private string CancelKey(string jobId) => $"{_keyPrefix}:{jobId}:cancel";
    private string QueueSetKey(string queueName) => $"{_keyPrefix}:queues:{queueName}";
    private string StatusSetKey(string queueName, QueueJobStatus status) => $"{_keyPrefix}:queues:{queueName}:status:{(int)status}";
    private string CounterBucketKey(string queueName, DateTimeOffset timestamp) => $"{_keyPrefix}:counters:{queueName}:{TruncateToHour(timestamp):yyyy-MM-ddTHH}";

    private static DateTimeOffset TruncateToHour(DateTimeOffset timestamp)
        => new(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, 0, 0, TimeSpan.Zero);

    private static QueueJobState ParseJobState(HashEntry[] entries)
    {
        var dict = new Dictionary<string, string>(entries.Length);
        Dictionary<string, string>? metadata = null;

        foreach (var entry in entries)
        {
            var name = entry.Name.ToString();
            if (name.StartsWith(MetadataFieldPrefix, StringComparison.Ordinal))
                (metadata ??= new Dictionary<string, string>())[name[MetadataFieldPrefix.Length..]] = entry.Value.ToString();
            else
                dict[name] = entry.Value.ToString();
        }

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
            Attempt = int.TryParse(dict.GetValueOrDefault("Attempt"), out var a) ? a : 0,
            LastUpdatedUtc = ParseDateTimeOffset(dict.GetValueOrDefault("LastUpdatedUtc")),
            Metadata = metadata
        };
    }

    private static DateTimeOffset ParseDateTimeOffset(string? value)
        => long.TryParse(value, out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.MinValue;

    private static DateTimeOffset? ParseNullableDateTimeOffset(string? value)
        => string.IsNullOrEmpty(value) ? null : long.TryParse(value, out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrEmpty(value) ? null : value;
}
