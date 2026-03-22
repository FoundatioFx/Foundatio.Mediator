using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    EnableGenerationCounter = true,
    AuthorizationRequired = true,
    MiddlewareLifetime = MediatorLifetime.Singleton,
    ApiVersions = ["2025-01-15", "2025-06-01"],
    ApiVersionHeader = "Api-Version"
)]
