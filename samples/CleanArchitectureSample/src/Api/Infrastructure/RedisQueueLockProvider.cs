using Foundatio.Mediator.Distributed;
using StackExchange.Redis;

namespace Api.Infrastructure;

/// <summary>
/// <see cref="IQueueLockProvider"/> over Redis for <c>[QueueLock]</c>: SET NX PX to acquire, a
/// compare-and-delete script to release (so an owner whose lock expired and was re-taken elsewhere never
/// releases the new owner's lock), and a compare-and-PEXPIRE script to renew. Registered as a singleton,
/// it makes the lock hold across every worker replica.
/// </summary>
public sealed class RedisQueueLockProvider(IConnectionMultiplexer redis) : IQueueLockProvider
{
    private const string KeyPrefix = "fm:locks:";
    private static readonly TimeSpan s_retryDelay = TimeSpan.FromMilliseconds(50);

    private const string ReleaseScript = "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";
    private const string RenewScript = "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('PEXPIRE', KEYS[1], ARGV[2]) else return 0 end";

    public async Task<IQueueLock?> TryAcquireAsync(string key, TimeSpan lifetime, TimeSpan acquireTimeout, CancellationToken cancellationToken = default)
    {
        var db = redis.GetDatabase();
        var redisKey = (RedisKey)(KeyPrefix + key);
        var owner = Guid.NewGuid().ToString("N");
        var deadline = DateTimeOffset.UtcNow + acquireTimeout;

        while (true)
        {
            if (await db.StringSetAsync(redisKey, owner, lifetime, When.NotExists).ConfigureAwait(false))
                return new RedisLock(db, key, redisKey, owner);

            if (DateTimeOffset.UtcNow >= deadline)
                return null;

            await Task.Delay(s_retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class RedisLock(IDatabase db, string key, RedisKey redisKey, string owner) : IQueueLock
    {
        public string Key => key;

        public async Task RenewAsync(TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            var renewed = (int)await db.ScriptEvaluateAsync(RenewScript, [redisKey], [owner, (long)lifetime.TotalMilliseconds]).ConfigureAwait(false);
            if (renewed == 0)
                throw new QueueLeaseLostException("The resource lock is no longer held by this owner.");
        }

        public async ValueTask DisposeAsync()
            => await db.ScriptEvaluateAsync(ReleaseScript, [redisKey], [owner]).ConfigureAwait(false);
    }
}
