namespace Foundatio.Mediator;

/// <summary>
/// Controls how handlers are discovered for endpoint generation.
/// </summary>
public enum EndpointDiscovery
{
    /// <summary>
    /// No endpoints are generated.
    /// </summary>
    None = 0,

    /// <summary>
    /// Only handlers explicitly marked with <see cref="HandlerEndpointAttribute"/> generate endpoints.
    /// </summary>
    Explicit = 1,

    /// <summary>
    /// All discovered handlers generate endpoints unless explicitly excluded.
    /// </summary>
    All = 2
}
