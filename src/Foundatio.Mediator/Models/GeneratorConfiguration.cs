namespace Foundatio.Mediator.Models;

internal record GeneratorConfiguration(
    bool InterceptorsEnabled,
    string HandlerLifetime,
    bool OpenTelemetryEnabled);