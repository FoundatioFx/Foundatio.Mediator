namespace Foundatio.Mediator;

/// <summary>
/// Marks a class or method as middleware for discovery and allows controlling execution order and lifetime.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MiddlewareAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MiddlewareAttribute"/> class.
    /// </summary>
    public MiddlewareAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MiddlewareAttribute"/> class with a specific order.
    /// </summary>
    /// <param name="order">The order in which this middleware should execute. Lower values execute first in Before methods, last in After/Finally methods.</param>
    public MiddlewareAttribute(int order)
    {
        Order = order;
    }

    /// <summary>
    /// Gets or sets the order in which this middleware should execute.
    /// Lower values execute first in Before methods, last in After/Finally methods.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets the dependency injection lifetime for this middleware.
    /// When set to <see cref="MediatorLifetime.Default"/>, uses the project-level
    /// MediatorDefaultMiddlewareLifetime MSBuild property.
    /// </summary>
    public MediatorLifetime Lifetime { get; set; } = MediatorLifetime.Default;

    /// <summary>
    /// Gets or sets whether this middleware should only be applied when explicitly referenced
    /// via [UseMiddleware] or a custom attribute implementing IHandlerMiddlewareAttribute.
    /// When true, the middleware will not be automatically applied based on message type matching.
    /// Default is false (middleware is applied globally based on message type).
    /// </summary>
    public bool ExplicitOnly { get; set; }
}
