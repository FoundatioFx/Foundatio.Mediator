using Common.Module.Middleware;
using Foundatio.Mediator;

// Global rate limiting default — all endpoints get "default" policy unless overridden
[assembly: EndpointRateLimiter]

[assembly: MediatorConfiguration(
    AuthorizationRequired = true,
    EnableGenerationCounter = true,
    MiddlewareLifetime = MediatorLifetime.Singleton
)]
