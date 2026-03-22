using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    HandlerDiscovery = HandlerDiscovery.Explicit,
    DisableOpenTelemetry = true,
    EndpointDiscovery = EndpointDiscovery.Explicit,
    EndpointRoutePrefix = "api",
    ApiVersions = ["1", "2"],
    ApiVersionHeader = "Api-Version"
)]
