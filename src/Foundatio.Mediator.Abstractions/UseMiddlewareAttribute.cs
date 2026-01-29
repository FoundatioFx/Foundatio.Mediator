namespace Foundatio.Mediator;

/// <summary>
/// Specifies middleware to apply to a handler method or class.
/// Can be applied multiple times to add multiple middleware.
/// Can also be applied to custom attribute classes to create middleware attributes like [Retry], [Cached], etc.
/// </summary>
/// <example>
/// <code>
/// // Direct usage on handlers
/// [UseMiddleware(typeof(StopwatchMiddleware))]
/// [UseMiddleware(typeof(ValidationMiddleware), Order = 10)]
/// public class OrderHandler
/// {
///     public Result&lt;Order&gt; Handle(CreateOrder command) { ... }
/// }
///
/// // Custom middleware attribute
/// // Apply [UseMiddleware] to your custom attribute class
/// [UseMiddleware(typeof(RetryMiddleware))]
/// [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
/// public class RetryAttribute : Attribute
/// {
///     public int MaxAttempts { get; set; } = 3;
///     public int DelayMs { get; set; } = 100;
/// }
///
/// // Usage:
/// [Retry(MaxAttempts = 5)]
/// public class OrderHandler { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class UseMiddlewareAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UseMiddlewareAttribute"/> class.
    /// </summary>
    /// <param name="middlewareType">The type of middleware to apply. Must be a valid middleware class.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="middlewareType"/> is null.</exception>
    public UseMiddlewareAttribute(Type middlewareType)
    {
        MiddlewareType = middlewareType ?? throw new ArgumentNullException(nameof(middlewareType));
    }

    /// <summary>
    /// Gets the type of middleware to apply to the handler.
    /// </summary>
    public Type MiddlewareType { get; }

    /// <summary>
    /// Gets or sets the execution order for this middleware relative to other middleware.
    /// Lower values execute first in Before methods, last in After/Finally methods.
    /// Default is int.MaxValue to run after global middleware (closest to the handler).
    /// </summary>
    public int Order { get; set; } = int.MaxValue;
}
