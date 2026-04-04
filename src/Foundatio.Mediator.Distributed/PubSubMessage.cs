namespace Foundatio.Mediator.Distributed;

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
