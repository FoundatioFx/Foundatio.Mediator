using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator.Models;

internal record GeneratorConfiguration(
    bool InterceptorsEnabled,
    string DefaultHandlerLifetime,
    string DefaultMiddlewareLifetime,
    bool OpenTelemetryEnabled,
    bool AuthorizationEnabled,
    bool ConventionalDiscoveryDisabled,
    bool GenerationCounterEnabled,
    string NotificationPublishStrategy,
    EquatableArray<string> HandlerExcludeNamespacePatterns);
