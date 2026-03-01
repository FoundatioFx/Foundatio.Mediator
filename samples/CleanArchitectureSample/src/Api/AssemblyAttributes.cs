using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    EnableGenerationCounter = true,
    EndpointDiscovery = EndpointDiscovery.All,
    EndpointRequireAuth = true,
    MiddlewareLifetime = MediatorLifetime.Singleton,
    ProjectName = "Api"
)]
