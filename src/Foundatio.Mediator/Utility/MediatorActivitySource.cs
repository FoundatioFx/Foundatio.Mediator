using System.Diagnostics;

namespace Foundatio.Mediator;

internal static class MediatorActivitySource
{
    internal static readonly ActivitySource Instance = new("Foundatio.Mediator");
}