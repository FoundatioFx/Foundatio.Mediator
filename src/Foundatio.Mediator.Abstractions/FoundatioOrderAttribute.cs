namespace Foundatio.Mediator;

/// <summary>
/// Specifies the execution order for middleware classes.
/// Lower order values execute first in the Before phase and last in the Finally phase.
/// When not specified, middleware follows the default ordering: message-specific, interface-based, then object-based.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class FoundatioOrderAttribute : Attribute
{
    /// <summary>
    /// Gets the order value for this middleware.
    /// Lower values execute first in Before phase and last in Finally phase.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Initializes a new instance of the FoundatioOrderAttribute class.
    /// </summary>
    /// <param name="order">The order value. Lower values execute first in Before phase and last in Finally phase.</param>
    public FoundatioOrderAttribute(int order)
    {
        Order = order;
    }
}
