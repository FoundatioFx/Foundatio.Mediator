using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator;

/// <summary>
/// Runtime configuration options for the mediator, used with <see cref="MediatorExtensions.AddMediator(IServiceCollection, MediatorOptions)"/>.
/// Not to be confused with <see cref="MediatorConfigurationAttribute"/> which controls compile-time source generation.
/// </summary>
public sealed class MediatorOptions
{
    /// <summary>
    /// Gets or sets the list of assemblies to scan for mediator handlers.
    /// </summary>
    public List<Assembly> Assemblies { get; set; } = [];

    /// <summary>
    /// Gets or sets the lifetime of the mediator.
    /// When <c>null</c> (the default), the lifetime is auto-detected:
    /// <see cref="ServiceLifetime.Scoped"/> for ASP.NET Core applications,
    /// <see cref="ServiceLifetime.Singleton"/> otherwise.
    /// </summary>
    public ServiceLifetime? MediatorLifetime { get; set; }

    /// <summary>
    /// When <c>true</c>, logs all registered handlers at startup in a formatted, columnar layout.
    /// Uses <c>ILogger</c> when available (via <c>ILoggerFactory</c> in the service collection),
    /// falling back to the console.
    /// </summary>
    public bool LogHandlers { get; set; }

    /// <summary>
    /// When <c>true</c>, logs the middleware pipeline at startup in a formatted, columnar layout.
    /// Shows each middleware's name, hooks (Before/After/Finally/Execute), order, and message scope.
    /// </summary>
    public bool LogMiddleware { get; set; }
}
