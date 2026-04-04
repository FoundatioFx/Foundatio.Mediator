namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Represents the current state of a queue job being tracked.
/// </summary>
public sealed class QueueJobState
{
    /// <summary>
    /// The unique identifier for this job, generated at enqueue time.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// The name of the queue this job was sent to.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>
    /// The full name of the message type being processed.
    /// </summary>
    public string MessageType { get; init; } = string.Empty;

    /// <summary>
    /// The current status of the job.
    /// </summary>
    public QueueJobStatus Status { get; set; } = QueueJobStatus.Queued;

    /// <summary>
    /// Progress percentage (0–100). Updated by the handler via <see cref="QueueContext.ReportProgressAsync(int, string?, CancellationToken)"/>.
    /// </summary>
    public int Progress { get; set; }

    /// <summary>
    /// Optional message describing what the job is currently doing.
    /// </summary>
    public string? ProgressMessage { get; set; }

    /// <summary>
    /// When the job was created (enqueued).
    /// </summary>
    public DateTimeOffset CreatedUtc { get; init; }

    /// <summary>
    /// When the worker started processing the job.
    /// </summary>
    public DateTimeOffset? StartedUtc { get; set; }

    /// <summary>
    /// When the job reached a terminal state (Completed, Failed, or Cancelled).
    /// </summary>
    public DateTimeOffset? CompletedUtc { get; set; }

    /// <summary>
    /// Error message when the job has failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The last time this state was updated.
    /// </summary>
    public DateTimeOffset LastUpdatedUtc { get; set; }
}

/// <summary>
/// The lifecycle status of a queue job.
/// </summary>
public enum QueueJobStatus
{
    /// <summary>
    /// The message has been enqueued but not yet picked up by a worker.
    /// </summary>
    Queued = 0,

    /// <summary>
    /// A worker is currently processing the message.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// The handler completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// The handler threw an exception or the message was dead-lettered.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// The job was cancelled via <see cref="IQueueJobStateStore.RequestCancellationAsync"/>.
    /// </summary>
    Cancelled = 4
}
