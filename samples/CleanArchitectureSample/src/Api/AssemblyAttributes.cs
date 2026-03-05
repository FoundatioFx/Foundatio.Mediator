using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    EnableGenerationCounter = true,
    AuthorizationRequired = true,
    MiddlewareLifetime = MediatorLifetime.Singleton
)]
