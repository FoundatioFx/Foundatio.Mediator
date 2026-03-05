using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    AuthorizationRequired = true,
    EnableGenerationCounter = true,
    MiddlewareLifetime = MediatorLifetime.Singleton
)]
