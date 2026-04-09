namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Describes a queue that should be created or ensured by the transport.
/// </summary>
public class QueueDefinition
{
    /// <summary>
    /// The transport-level queue name.
    /// </summary>
    public required string Name { get; init; }

    /// <inheritdoc />
    public override string ToString() => Name;
}
