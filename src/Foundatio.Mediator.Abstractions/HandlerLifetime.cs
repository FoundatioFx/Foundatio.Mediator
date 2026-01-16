namespace Foundatio.Mediator;

/// <summary>
/// Specifies the lifetime of a handler when registered with dependency injection.
/// </summary>
public enum HandlerLifetime
{
    /// <summary>
    /// Use the default lifetime specified by MediatorDefaultHandlerLifetime MSBuild property.
    /// If no default is specified, the handler will not be automatically registered.
    /// </summary>
    Default = 0,

    /// <summary>
    /// A new instance is created every time the handler is requested.
    /// </summary>
    Transient = 1,

    /// <summary>
    /// A single instance is created per scope (e.g., per HTTP request).
    /// </summary>
    Scoped = 2,

    /// <summary>
    /// A single instance is created and shared for the lifetime of the application.
    /// </summary>
    Singleton = 3
}
