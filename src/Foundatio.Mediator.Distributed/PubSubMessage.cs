namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Represents an outbound message to be published to a topic.
/// </summary>
public sealed class PubSubEntry
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

/// <summary>
/// A message received from the pub/sub client.
/// </summary>
public sealed class PubSubMessage
{
    /// <summary>
    /// The serialized message body.
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    /// Transport headers / metadata.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
}
