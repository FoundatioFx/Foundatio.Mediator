namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Distributed lock used by <see cref="QueueLockAttribute"/>. Implement over any lock service
/// (Redis, a database, a cache provider) and register it as a singleton.
/// </summary>
public interface IQueueLockProvider
{
    /// <summary>
    /// Tries to acquire <paramref name="key"/> for <paramref name="lifetime"/>, waiting up to
    /// <paramref name="acquireTimeout"/> when it is held. Returns <c>null</c> when it could not be acquired.
    /// </summary>
    Task<IQueueLock?> TryAcquireAsync(string key, TimeSpan lifetime, TimeSpan acquireTimeout, CancellationToken cancellationToken = default);
}

/// <summary>
/// A held lock. Disposing releases it.
/// </summary>
public interface IQueueLock : IAsyncDisposable
{
    string Key { get; }

    /// <summary>
    /// Extends the lock by <paramref name="lifetime"/> from now.
    /// </summary>
    Task RenewAsync(TimeSpan lifetime, CancellationToken cancellationToken = default);
}
