using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    AuthorizationRequired = true,
    EnableGenerationCounter = true,
    MiddlewareLifetime = MediatorLifetime.Singleton,
    ApiVersions = ["2025-01-15", "2025-06-01"],
    ApiVersionHeader = "Api-Version"
)]
