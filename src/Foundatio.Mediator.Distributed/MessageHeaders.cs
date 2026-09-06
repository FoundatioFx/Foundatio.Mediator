namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Well-known message header names written by the library. Headers map to transport-native
/// attributes (SQS message attributes, SNS message attributes).
/// </summary>
public static class MessageHeaders
{
    public const string MessageType = "fm-message-type";
    public const string CorrelationId = "fm-correlation-id";
    public const string EnqueuedAt = "fm-enqueued-at";
    public const string OriginHostId = "fm-origin-host-id";
    public const string PublishedAt = "fm-published-at";
    public const string TraceParent = "traceparent";
    public const string TraceState = "tracestate";
    public const string DeadLetterReason = "fm-dead-letter-reason";
    public const string DeadLetteredAt = "fm-dead-lettered-at";
    public const string OriginalQueueName = "fm-original-queue-name";
    public const string DeadLetterDequeueCount = "fm-dead-letter-dequeue-count";
    public const string ReplayedAt = "fm-replayed-at";
    public const string OriginalJobId = "fm-original-job-id";
    public const string JobId = "fm-job-id";

    /// <summary>
    /// Every header as one JSON object, used by transports whose native attribute count is capped.
    /// </summary>
    public const string PackedHeaders = "fm-headers";
}
