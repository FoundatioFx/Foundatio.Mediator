using Common.Module.Messages;
using Foundatio.Mediator;

namespace Common.Module.Handlers;

/// <summary>
/// A public health check endpoint that demonstrates using <c>[HandlerAllowAnonymous]</c>
/// to opt a handler out of the global <c>AuthorizationRequired = true</c> setting.
///
/// Even though all handlers require authorization by default, this
/// handler is accessible without logging in — useful for load balancers, uptime
/// monitors, and readiness probes.
/// </summary>
[HandlerAllowAnonymous]
public class HealthHandler
{
    public HealthStatusResponse Handle(GetHealthStatus query) =>
        new("Healthy", "1.0.0", DateTime.UtcNow);
}
