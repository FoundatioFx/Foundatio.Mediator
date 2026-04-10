namespace Foundatio.Mediator;

/// <summary>
/// Diagnostic metadata for a registered middleware, populated at compile time by the source generator.
/// Used by <see cref="HandlerRegistry.ShowRegisteredMiddleware"/> to display the middleware pipeline.
/// </summary>
public sealed class MiddlewareRegistration
{
    /// <summary>
    /// Creates a new middleware registration.
    /// </summary>
    /// <param name="name">The short name of the middleware class (e.g., "TimingMiddleware").</param>
    /// <param name="hooks">Comma-separated list of hooks the middleware implements (e.g., "Before, After, Finally").</param>
    /// <param name="order">The numeric execution order. Null when using only relative ordering.</param>
    /// <param name="messageScope">The message type this middleware targets, or "object" for global middleware.</param>
    /// <param name="isStatic">Whether the middleware class is static.</param>
    /// <param name="isExplicitOnly">Whether the middleware is only applied via explicit [UseMiddleware] references.</param>
    public MiddlewareRegistration(string name, string hooks, int? order, string messageScope, bool isStatic, bool isExplicitOnly)
    {
        Name = name;
        Hooks = hooks;
        Order = order;
        MessageScope = messageScope;
        IsStatic = isStatic;
        IsExplicitOnly = isExplicitOnly;
    }

    /// <summary>
    /// The short name of the middleware class (e.g., "TimingMiddleware").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Comma-separated list of hooks the middleware implements (e.g., "Before, After, Finally").
    /// </summary>
    public string Hooks { get; }

    /// <summary>
    /// The numeric execution order. Null when using only relative ordering.
    /// </summary>
    public int? Order { get; }

    /// <summary>
    /// The message type this middleware targets (short name), or "object" for global middleware.
    /// </summary>
    public string MessageScope { get; }

    /// <summary>
    /// Whether the middleware class is static.
    /// </summary>
    public bool IsStatic { get; }

    /// <summary>
    /// Whether the middleware is only applied when explicitly referenced via [UseMiddleware].
    /// </summary>
    public bool IsExplicitOnly { get; }
}
