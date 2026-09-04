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
    /// Optional prefix applied before <see cref="KeyPrefix"/> for app-level scoping.
    /// When set, all Redis keys become <c>"{ResourcePrefix}:{KeyPrefix}:..."</c>.
    /// When <c>null</c> or empty (default), only <see cref="KeyPrefix"/> is used.
    /// </summary>
    /// <remarks>
    /// Use this to isolate multiple applications sharing the same Redis instance
    /// (e.g., <c>"myapp"</c> produces keys like <c>"myapp:fm:jobs:..."</c>).
    /// </remarks>
    public string? ResourcePrefix { get; set; }

    /// <summary>
    /// Default TTL for terminal job states (Completed, Failed, Cancelled).
    /// Default is 24 hours. Set to <c>null</c> to disable auto-expiry.
    /// </summary>
    public TimeSpan? DefaultExpiry { get; set; } = TimeSpan.FromHours(24);
}
