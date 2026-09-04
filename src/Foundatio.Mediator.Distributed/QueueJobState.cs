namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Tracked state of a queued job. Immutable; use <c>with</c> expressions to derive updates.
/// </summary>
public sealed record QueueJobState
{
    public required string JobId { get; init; }
    public required string QueueName { get; init; }
    public string MessageType { get; init; } = string.Empty;
    public QueueJobStatus Status { get; init; } = QueueJobStatus.Queued;
    public int Progress { get; init; }
    public string? ProgressMessage { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public int Attempt { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset LastUpdatedUtc { get; init; }

    /// <summary>
    /// Caller-supplied metadata captured at enqueue time via <see cref="DistributedQueueOptions.JobMetadataProvider"/>,
    /// such as a tenant or user id, so stores can index and display jobs by them.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public enum QueueJobStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}
