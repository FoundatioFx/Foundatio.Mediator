namespace Foundatio.Mediator.Models;

/// <summary>
/// Represents a reference to middleware specified on a handler via [UseMiddleware] or custom attributes
/// implementing IHandlerMiddlewareAttribute.
/// </summary>
internal readonly record struct HandlerMiddlewareReference
{
    /// <summary>
    /// The fully qualified type name of the middleware.
    /// </summary>
    public string MiddlewareTypeName { get; init; }

    /// <summary>
    /// The order specified on the attribute.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Whether this was specified on the method (true) or class (false).
    /// Method-level attributes take precedence for ordering.
    /// </summary>
    public bool IsMethodLevel { get; init; }
}
