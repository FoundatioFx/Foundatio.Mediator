using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator;

/// <summary>
/// Runtime configuration options for the mediator, used with <see cref="MediatorExtensions.AddMediator"/>.
/// Not to be confused with <see cref="MediatorConfigurationAttribute"/> which controls compile-time source generation.
/// </summary>
public sealed class MediatorOptions
{
    /// <summary>
    /// Gets or sets the list of assemblies to scan for mediator handlers.
    /// </summary>
    public List<Assembly>? Assemblies { get; set; }

    /// <summary>
    /// Gets or sets the lifetime of the mediator.
    /// </summary>
    public ServiceLifetime MediatorLifetime { get; set; } = ServiceLifetime.Singleton;
}
