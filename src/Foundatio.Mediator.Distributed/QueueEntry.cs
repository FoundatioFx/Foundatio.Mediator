namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Represents an outbound message to be sent to a queue.
/// The <see cref="Body"/> contains the pure serialized message payload.
/// Metadata (type discriminator, correlation id, etc.) is carried as
/// <see cref="Headers"/>, which map to transport-native message attributes
/// (SQS MessageAttributes, RabbitMQ headers, etc.).
/// </summary>
public sealed class QueueEntry
{
    /// <summary>
    /// The serialized message body.
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    /// Optional metadata headers. Well-known keys are defined in <see cref="MessageHeaders"/>.
    /// </summary>
    public Dictionary<string, string>? Headers { get; init; }
}
