namespace Foundatio.Mediator;

/// <summary>
/// Marks a class or method as a handler for discovery and allows controlling execution order and lifetime.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class HandlerAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerAttribute"/> class.
    /// </summary>
    public HandlerAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HandlerAttribute"/> class with a specific order.
    /// </summary>
    /// <param name="order">The order in which this handler should execute during PublishAsync. Lower values execute first.</param>
    public HandlerAttribute(int order)
    {
        Order = order;
    }

    /// <summary>
    /// Gets or sets the order in which this handler should execute during PublishAsync.
    /// Lower values execute first. Handlers without explicit order execute last.
    /// </summary>
    public int Order { get; set; } = int.MaxValue;

    /// <summary>
    /// Gets or sets the dependency injection lifetime for this handler.
    /// When set to <see cref="MediatorLifetime.Default"/>, uses the project-level
    /// MediatorDefaultHandlerLifetime MSBuild property.
    /// </summary>
    public MediatorLifetime Lifetime { get; set; } = MediatorLifetime.Default;
}
