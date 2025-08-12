using System.Reflection;
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
    public ServiceLifetime MediatorLifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>
    /// Gets or sets the notification publisher.
    /// </summary>
    public INotificationPublisher NotificationPublisher { get; set; } = new TaskWhenAllPublisher();
}
