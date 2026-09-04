namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Well-known header keys used by the distributed messaging infrastructure.
/// These map to transport-native message attributes (SQS MessageAttributes,
/// RabbitMQ headers, etc.).
/// </summary>
public static class MessageHeaders
{
    /// <summary>
    /// The assembly-qualified type name of the message, used for deserialization.
    /// </summary>
    public const string MessageType = "fm-message-type";

    /// <summary>
    /// Optional correlation identifier for tracing a message through the system.
    /// </summary>
    public const string CorrelationId = "fm-correlation-id";

    /// <summary>
    /// ISO 8601 timestamp of when the message was enqueued.
    /// </summary>
    public const string EnqueuedAt = "fm-enqueued-at";

    /// <summary>
    /// The unique identifier of the host that originally published the notification.
    /// Used to prevent a node from re-processing its own message.
    /// </summary>
    public const string OriginHostId = "fm-origin-host-id";

    /// <summary>
    /// ISO 8601 timestamp of when the notification was published to the bus.
    /// </summary>
    public const string PublishedAt = "fm-published-at";

    /// <summary>
    /// W3C traceparent header for distributed trace context propagation.
    /// </summary>
    public const string TraceParent = "traceparent";

    /// <summary>
    /// W3C tracestate header for vendor-specific trace context.
    /// </summary>
    public const string TraceState = "tracestate";

    /// <summary>
    /// The reason a message was moved to the dead-letter queue.
    /// </summary>
    public const string DeadLetterReason = "fm-dead-letter-reason";

    /// <summary>
    /// ISO 8601 timestamp of when the message was dead-lettered.
    /// </summary>
    public const string DeadLetteredAt = "fm-dead-lettered-at";

    /// <summary>
    /// The original queue name before the message was dead-lettered.
    /// </summary>
    public const string OriginalQueueName = "fm-original-queue-name";

    /// <summary>
    /// The number of times the message was dequeued before being dead-lettered.
    /// </summary>
    public const string DeadLetterDequeueCount = "fm-dead-letter-dequeue-count";

    /// <summary>
    /// The unique job identifier assigned at enqueue time for progress tracking.
    /// </summary>
    public const string JobId = "fm-job-id";
}
