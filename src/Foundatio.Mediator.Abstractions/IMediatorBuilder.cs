using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator;

/// <summary>
/// A builder for configuring Foundatio.Mediator services.
/// Extension methods on this interface allow distributed, transport, and other
/// packages to add fluent configuration to the <c>AddMediator()</c> call chain.
/// </summary>
public interface IMediatorBuilder
{
    /// <summary>
    /// Gets the underlying service collection.
    /// </summary>
    IServiceCollection Services { get; }
}

/// <inheritdoc />
public sealed class MediatorBuilder : IMediatorBuilder
{
    /// <summary>
    /// Creates a new <see cref="MediatorBuilder"/> wrapping the specified service collection.
    /// </summary>
    public MediatorBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <inheritdoc />
    public IServiceCollection Services { get; }
}
