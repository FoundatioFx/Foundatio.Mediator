namespace Foundatio.Mediator.Models;

internal record GeneratorConfiguration(
    bool InterceptorsEnabled,
    string DefaultHandlerLifetime,
    bool OpenTelemetryEnabled,
    bool ConventionalDiscoveryDisabled,
    bool GenerationCounterEnabled);
