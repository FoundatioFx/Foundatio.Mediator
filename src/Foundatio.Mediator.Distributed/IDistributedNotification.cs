namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Marker interface for notifications that should be distributed across all nodes
/// in a scale-out cluster. Extends <see cref="INotification"/> so that
/// <c>mediator.PublishAsync()</c> dispatches to local handlers as usual,
/// while the distributed infrastructure fans the message out to remote nodes.
/// </summary>
/// <example>
/// <code>
/// public record OrderCreated(Guid OrderId, Guid CustomerId) : IDistributedNotification;
/// </code>
/// </example>
public interface IDistributedNotification : INotification { }
