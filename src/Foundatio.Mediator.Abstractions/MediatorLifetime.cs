namespace Foundatio.Mediator;

/// <summary>
/// Specifies the lifetime of a handler or middleware when registered with dependency injection.
/// </summary>
public enum MediatorLifetime
{
    /// <summary>
    /// Use the default lifetime specified by the project-level MSBuild property
    /// (MediatorDefaultHandlerLifetime for handlers, MediatorDefaultMiddlewareLifetime for middleware).
    /// If no default is specified, the component will not be automatically registered.
    /// </summary>
    Default = 0,

    /// <summary>
    /// A new instance is created every time the component is requested.
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
