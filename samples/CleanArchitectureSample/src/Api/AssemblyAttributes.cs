using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    EnableGenerationCounter = true,
    EndpointDiscovery = EndpointDiscovery.All,
    AuthorizationRequired = true,
    MiddlewareLifetime = MediatorLifetime.Singleton,
    ProjectName = "Api"
)]
