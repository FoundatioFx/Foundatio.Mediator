namespace Common.Module.Services;

/// <summary>
/// Interface for audit logging.
/// Following Clean Architecture, modules depend on this abstraction,
/// allowing the audit implementation to be swapped without affecting business logic.
/// </summary>
public interface IAuditService
{
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntry>> GetRecentEntriesAsync(int count = 50, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an audit log entry.
/// </summary>
public record AuditEntry(
    string Id,
    string EventType,
    string EntityType,
    string EntityId,
    string Description,
    DateTime Timestamp,
    Dictionary<string, object?>? Metadata = null);
