namespace Foundatio.Mediator;

/// <summary>
/// Runtime configuration options for <c>MapMediatorEndpoints</c>,
/// controlling which assemblies have their endpoints mapped and logging behavior.
/// </summary>
public sealed class MediatorEndpointOptions
{
    /// <summary>
    /// Gets or sets the list of assemblies to scan for endpoint modules.
    /// When empty, all loaded assemblies with <see cref="FoundatioModuleAttribute"/> are scanned.
    /// </summary>
    public List<Assembly> Assemblies { get; set; } = [];

    /// <summary>
    /// When <c>true</c>, logs all mapped endpoints at startup.
    /// </summary>
    public bool LogEndpoints { get; set; }
}

/// <summary>
/// Builder for configuring <see cref="MediatorEndpointOptions"/>.
/// </summary>
public sealed class MediatorEndpointOptionsBuilder
{
    private readonly MediatorEndpointOptions _options = new();

    /// <summary>
    /// Adds the specified assemblies to scan for endpoint modules.
    /// </summary>
    public MediatorEndpointOptionsBuilder AddAssembly(params Assembly[] assemblies)
    {
        _options.Assemblies.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Adds the assembly containing the specified type to scan for endpoint modules.
    /// </summary>
    public MediatorEndpointOptionsBuilder AddAssembly<T>()
    {
        return AddAssembly(typeof(T).Assembly);
    }

    /// <summary>
    /// Enables logging of all mapped endpoints at startup.
    /// </summary>
    public MediatorEndpointOptionsBuilder LogEndpoints(bool log = true)
    {
        _options.LogEndpoints = log;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="MediatorEndpointOptions"/>.
    /// </summary>
    public MediatorEndpointOptions Build() => _options;
}
