using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    HandlerDiscovery = HandlerDiscovery.Explicit,
    DisableOpenTelemetry = true,
    EndpointDiscovery = EndpointDiscovery.Explicit,
    EndpointRoutePrefix = "api"
)]
