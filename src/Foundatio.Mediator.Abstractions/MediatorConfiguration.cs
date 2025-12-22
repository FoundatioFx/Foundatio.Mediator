using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator;

public class MediatorConfiguration
{
    /// <summary>
    /// Gets or sets the list of assemblies to scan for mediator handlers.
    /// </summary>
    public List<Assembly>? Assemblies { get; set; }

    /// <summary>
    /// Gets or sets the lifetime of the mediator.
    /// </summary>
    public ServiceLifetime MediatorLifetime { get; set; } = ServiceLifetime.Singleton;

    /// <summary>
    /// Gets or sets the notification publisher. Default is ForeachAwaitPublisher (sequential).
    /// Use TaskWhenAllPublisher for parallel execution.
    /// </summary>
    public INotificationPublisher NotificationPublisher { get; set; } = new ForeachAwaitPublisher();
}
