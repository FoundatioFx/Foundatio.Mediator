namespace Foundatio.Mediator.Models;

internal record GeneratorConfiguration(
    bool InterceptorsEnabled,
    string DefaultHandlerLifetime,
    string DefaultMiddlewareLifetime,
    bool OpenTelemetryEnabled,
    bool ConventionalDiscoveryDisabled,
    bool GenerationCounterEnabled,
    string NotificationPublisher,
    string EndpointDiscoveryMode,
    bool EndpointRequireAuthDefault,
    string? ProjectName);
