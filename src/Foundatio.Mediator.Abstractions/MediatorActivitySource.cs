using System.Diagnostics;

namespace Foundatio.Mediator;

/// <summary>
/// Provides the shared <see cref="ActivitySource"/> used for OpenTelemetry tracing in Foundatio.Mediator.
/// </summary>
public static class MediatorActivitySource
{
    /// <summary>
    /// The shared <see cref="ActivitySource"/> instance named <c>"Foundatio.Mediator"</c>.
    /// </summary>
    public static readonly ActivitySource Instance = new("Foundatio.Mediator");
}
