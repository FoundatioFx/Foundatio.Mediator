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

        var entries = new HashEntry[]
        {
            new("JobId", state.JobId),
            new("QueueName", state.QueueName),
            new("MessageType", state.MessageType),
            new("Status", ((int)state.Status).ToString(CultureInfo.InvariantCulture)),
            new("Progress", state.Progress.ToString(CultureInfo.InvariantCulture)),
            new("ProgressMessage", state.ProgressMessage ?? string.Empty),
            new("CreatedUtc", state.CreatedUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)),
            new("StartedUtc", state.StartedUtc?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            new("CompletedUtc", state.CompletedUtc?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            new("ErrorMessage", state.ErrorMessage ?? string.Empty),
            new("LastUpdatedUtc", state.LastUpdatedUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture))
        };

        await db.HashSetAsync(key, entries).ConfigureAwait(false);

        // Add to the per-queue sorted set (scored by creation timestamp for ordering)
        var queueSetKey = QueueSetKey(state.QueueName);
        await db.SortedSetAddAsync(queueSetKey, state.JobId, state.CreatedUtc.ToUnixTimeMilliseconds()).ConfigureAwait(false);

        // Set TTL
        var ttl = expiry ?? _options.DefaultExpiry;
        if (ttl.HasValue)
        {
            await db.KeyExpireAsync(key, ttl.Value).ConfigureAwait(false);
            await db.KeyExpireAsync(queueSetKey, ttl.Value).ConfigureAwait(false);
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

    public async Task<IReadOnlyList<QueueJobState>> GetJobsByQueueAsync(string queueName, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var queueSetKey = QueueSetKey(queueName);

        // Get job IDs from sorted set in reverse order (newest first)
        var jobIds = await db.SortedSetRangeByRankAsync(queueSetKey, skip, skip + take - 1, Order.Descending).ConfigureAwait(false);

        var results = new List<QueueJobState>(jobIds.Length);
        foreach (var jobId in jobIds)
        {
            if (jobId.IsNullOrEmpty)
                continue;

            var state = await GetJobStateAsync(jobId.ToString(), cancellationToken).ConfigureAwait(false);
            if (state is not null)
                results.Add(state);
        }

        return results;
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

        // Get queue name before deleting so we can clean up the sorted set
        var queueName = await db.HashGetAsync(key, "QueueName").ConfigureAwait(false);

        await db.KeyDeleteAsync(key).ConfigureAwait(false);
        await db.KeyDeleteAsync(CancelKey(jobId)).ConfigureAwait(false);

        if (!queueName.IsNullOrEmpty)
            await db.SortedSetRemoveAsync(QueueSetKey(queueName.ToString()), jobId).ConfigureAwait(false);
    }

    public Task IncrementCounterAsync(string queueName, string counterName, long value = 1, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = CountersKey(queueName);
        return db.HashIncrementAsync(key, counterName, value);
    }

    public async Task<IReadOnlyDictionary<string, long>> GetCountersAsync(string queueName, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = CountersKey(queueName);
        var entries = await db.HashGetAllAsync(key).ConfigureAwait(false);

        var result = new Dictionary<string, long>(entries.Length);
        foreach (var entry in entries)
        {
            if (entry.Value.TryParse(out long val))
                result[entry.Name.ToString()] = val;
        }

        return result;
    }

    private string JobKey(string jobId) => $"{_options.KeyPrefix}:{jobId}";
    private string CancelKey(string jobId) => $"{_options.KeyPrefix}:{jobId}:cancel";
    private string QueueSetKey(string queueName) => $"{_options.KeyPrefix}:queues:{queueName}";
    private string CountersKey(string queueName) => $"{_options.KeyPrefix}:counters:{queueName}";

    public async Task<IReadOnlyList<QueueJobState>> GetJobsByStatusAsync(string queueName, QueueJobStatus[] statuses, int skip = 0, int take = 50, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var queueSetKey = QueueSetKey(queueName);
        var prefix = _options.KeyPrefix;

        // Build status filter string for Lua (e.g., ",0,1,")
        var statusFilter = "," + string.Join(",", statuses.Select(s => ((int)s).ToString(CultureInfo.InvariantCulture))) + ",";

        // Lua script: iterate sorted set in descending order, check Status hash field, collect matching job IDs
        const string lua = """
            local queueKey = KEYS[1]
            local prefix = ARGV[1]
            local statusFilter = ARGV[2]
            local skip = tonumber(ARGV[3])
            local take = tonumber(ARGV[4])

            local all = redis.call('ZREVRANGE', queueKey, 0, -1)
            local matched = 0
            local collected = 0
            local results = {}

            for _, jobId in ipairs(all) do
                local status = redis.call('HGET', prefix .. ':' .. jobId, 'Status')
                if status and string.find(statusFilter, ',' .. status .. ',', 1, true) then
                    matched = matched + 1
                    if matched > skip then
                        table.insert(results, jobId)
                        collected = collected + 1
                        if collected >= take then break end
                    end
                end
            end

            return results
            """;

        var scriptResult = await db.ScriptEvaluateAsync(lua,
            [queueSetKey],
            [prefix, statusFilter, skip, take]).ConfigureAwait(false);

        var jobIds = (RedisResult[]?)scriptResult;
        if (jobIds is null || jobIds.Length == 0)
            return [];

        var results = new List<QueueJobState>(jobIds.Length);
        foreach (var jobId in jobIds)
        {
            var state = await GetJobStateAsync(jobId.ToString()!, cancellationToken).ConfigureAwait(false);
            if (state is not null)
                results.Add(state);
        }

        return results;
    }

    public async Task<long> GetJobCountByStatusAsync(string queueName, QueueJobStatus status, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var queueSetKey = QueueSetKey(queueName);
        var prefix = _options.KeyPrefix;
        var statusStr = ((int)status).ToString(CultureInfo.InvariantCulture);

        const string lua = """
            local queueKey = KEYS[1]
            local prefix = ARGV[1]
            local targetStatus = ARGV[2]

            local all = redis.call('ZRANGE', queueKey, 0, -1)
            local count = 0

            for _, jobId in ipairs(all) do
                local status = redis.call('HGET', prefix .. ':' .. jobId, 'Status')
                if status == targetStatus then
                    count = count + 1
                end
            end

            return count
            """;

        var result = await db.ScriptEvaluateAsync(lua,
            [queueSetKey],
            [prefix, statusStr]).ConfigureAwait(false);

        return (long)result;
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
