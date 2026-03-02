using System.ComponentModel;

namespace Foundatio.Mediator;

/// <summary>
/// Provides access to the underlying <see cref="IServiceProvider"/> for infrastructure use.
/// Implemented explicitly by <see cref="Mediator"/> and <see cref="ScopedMediator"/>
/// to keep it hidden from normal API consumers.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IServiceProviderAccessor
{
    /// <summary>
    /// Gets the underlying service provider.
    /// </summary>
    IServiceProvider ServiceProvider { get; }
}
