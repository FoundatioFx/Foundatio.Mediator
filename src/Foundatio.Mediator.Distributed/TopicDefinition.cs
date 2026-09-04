namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Describes a topic that should be created or ensured by the transport.
/// </summary>
public class TopicDefinition
{
    /// <summary>
    /// The transport-level topic name.
    /// </summary>
    public required string Name { get; init; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
