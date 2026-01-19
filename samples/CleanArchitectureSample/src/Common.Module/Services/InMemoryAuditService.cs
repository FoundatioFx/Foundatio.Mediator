using System.Collections.Concurrent;

namespace Common.Module.Services;

/// <summary>
/// In-memory implementation of IAuditService for demonstration.
/// In production, this would write to a database, event store, or external audit system.
/// </summary>
public class InMemoryAuditService : IAuditService
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();
    private const int MaxEntries = 1000;

    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _entries.Enqueue(entry);

        // Keep queue bounded
        while (_entries.Count > MaxEntries && _entries.TryDequeue(out _)) { }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetRecentEntriesAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        var entries = _entries
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();

        return Task.FromResult<IReadOnlyList<AuditEntry>>(entries);
    }
}
