using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    EndpointDiscovery = EndpointDiscovery.All,
    EndpointRequireAuth = true,
    EnableGenerationCounter = true,
    MiddlewareLifetime = MediatorLifetime.Singleton,
    ProjectName = "Reports"
)]
