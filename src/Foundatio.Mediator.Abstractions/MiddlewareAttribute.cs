namespace Foundatio.Mediator;

/// <summary>
/// Marks a class or method as middleware for cross-assembly discovery.
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
}
