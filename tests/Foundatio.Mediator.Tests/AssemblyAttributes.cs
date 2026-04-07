using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    HandlerLifetime = MediatorLifetime.Scoped,
    MiddlewareLifetime = MediatorLifetime.Scoped,
    EndpointRoutePrefix = "api",
    ApiVersions = ["1", "2"],
    ApiVersionHeader = "Api-Version"
)]
