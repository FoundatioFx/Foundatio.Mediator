namespace Foundatio.Mediator.Distributed.Redis;

/// <summary>
/// Options for configuring <see cref="RedisQueueJobStateStore"/>.
/// </summary>
public class RedisJobStateStoreOptions
{
    /// <summary>
    /// Key prefix for all Redis keys. Default is "fm:jobs".
    /// </summary>
    public string KeyPrefix { get; set; } = "fm:jobs";

    /// <summary>
    /// Default TTL for terminal job states (Completed, Failed, Cancelled).
    /// Default is 24 hours. Set to <c>null</c> to disable auto-expiry.
    /// </summary>
    public TimeSpan? DefaultExpiry { get; set; } = TimeSpan.FromHours(24);
}
