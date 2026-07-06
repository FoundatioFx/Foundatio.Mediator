namespace Foundatio.Mediator;

/// <summary>
/// Specifies the lifetime of a handler or middleware when registered with dependency injection.
/// </summary>
public enum MediatorLifetime
{
    /// <summary>
    /// Use the default lifetime from <see cref="MediatorConfigurationAttribute"/>.
    /// If no default is specified, the component is internally cached (best performance).
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
    Singleton = 3,

    /// <summary>
    /// The mediator creates a fresh DI scope for each invocation and resolves the entire
    /// pipeline — middleware and handler, including their dependencies — from that scope.
    /// The scope is disposed when the invocation (including any cascading messages) completes.
    /// <para>
    /// Use this for handlers written against scope-per-message semantics (MassTransit mediator,
    /// Wolverine) that assume isolated scoped dependencies per dispatch, such as a fresh
    /// <c>DbContext</c> with its own change tracker. With the other lifetimes, DI scope
    /// management is the caller's responsibility and dispatch uses the caller's ambient scope.
    /// </para>
    /// <para>
    /// Nested and cascading dispatches from a <see cref="ScopedPerInvoke"/> handler use its
    /// scope as their ambient scope; targets that are themselves <see cref="ScopedPerInvoke"/>
    /// get their own fresh scope. Avoid this lifetime for handlers returning deferred results
    /// (<c>IQueryable&lt;T&gt;</c>, <c>IAsyncEnumerable&lt;T&gt;</c>) — the backing services are
    /// disposed with the scope before the result is enumerated.
    /// </para>
    /// Only valid for handlers; not supported on middleware.
    /// </summary>
    ScopedPerInvoke = 4
}
