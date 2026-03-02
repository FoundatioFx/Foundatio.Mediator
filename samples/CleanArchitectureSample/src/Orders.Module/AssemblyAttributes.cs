using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    EndpointDiscovery = EndpointDiscovery.All,
    AuthorizationRequired = true,
    EnableGenerationCounter = true,
    ProjectName = "Orders"
)]
