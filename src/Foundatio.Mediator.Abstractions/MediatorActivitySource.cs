using System.Diagnostics;

namespace Foundatio.Mediator;

public static class MediatorActivitySource
{
    public static readonly ActivitySource Instance = new("Foundatio.Mediator");
}