using Common.Module;
using Foundatio.Mediator;

[assembly: MediatorConfiguration(
    EnableGenerationCounter = true,
    AuthorizationRequired = true,
    MiddlewareLifetime = MediatorLifetime.Singleton,
    ApiVersions = [ApiConstants.V1, ApiConstants.V2],
    ApiVersionHeader = ApiConstants.VersionHeader
)]
