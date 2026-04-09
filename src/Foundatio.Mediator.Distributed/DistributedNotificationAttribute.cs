namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Marks a notification type for distributed fan-out via pub/sub.
/// This is an alternative to implementing <see cref="IDistributedNotification"/> —
/// use this attribute when you cannot or prefer not to modify the type hierarchy.
/// </summary>
/// <example>
/// <code>
/// [DistributedNotification]
/// public record OrderCreated(string OrderId, DateTime CreatedAt);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class DistributedNotificationAttribute : Attribute;
