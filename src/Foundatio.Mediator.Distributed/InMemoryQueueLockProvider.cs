using System.Collections.Concurrent;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Process-local <see cref="IQueueLockProvider"/> for development and tests. It does not coordinate
/// across processes, so it is registered automatically only when the in-memory queue client is in use.
/// </summary>
public sealed class InMemoryQueueLockProvider(TimeProvider? timeProvider = null) : IQueueLockProvider
{
    private readonly ConcurrentDictionary<string, Entry> _locks = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<IQueueLock?> TryAcquireAsync(string key, TimeSpan lifetime, TimeSpan acquireTimeout, CancellationToken cancellationToken = default)
    {
        var deadline = _timeProvider.GetUtcNow() + acquireTimeout;
        while (true)
        {
            var now = _timeProvider.GetUtcNow();
            var entry = new Entry(Guid.NewGuid(), now + lifetime);

            if (_locks.TryAdd(key, entry))
                return new Lock(this, key, entry);

            if (_locks.TryGetValue(key, out var existing) && existing.ExpiresAt <= now && _locks.TryUpdate(key, entry, existing))
                return new Lock(this, key, entry);

            if (_timeProvider.GetUtcNow() >= deadline)
                return null;

            await Task.Delay(TimeSpan.FromMilliseconds(25), _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether <paramref name="key"/> is currently held.
    /// </summary>
    public bool IsLocked(string key)
        => _locks.TryGetValue(key, out var entry) && entry.ExpiresAt > _timeProvider.GetUtcNow();

    private sealed record Entry(Guid Owner, DateTimeOffset ExpiresAt);

    private sealed class Lock(InMemoryQueueLockProvider provider, string key, Entry entry) : IQueueLock
    {
        private Entry _entry = entry;

        public string Key => key;

        public Task RenewAsync(TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            var renewed = _entry with { ExpiresAt = provider._timeProvider.GetUtcNow() + lifetime };
            if (provider._locks.TryUpdate(key, renewed, _entry))
                _entry = renewed;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            provider._locks.TryRemove(new KeyValuePair<string, Entry>(key, _entry));
            return default;
        }
    }
}
