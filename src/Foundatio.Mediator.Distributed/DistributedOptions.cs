using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Distributed;

/// <summary>Shared defaults for queues, notifications, and provider infrastructure.</summary>
public sealed class DistributedOptions
{
    /// <summary>Application/environment prefix inherited unless a feature or provider overrides it.</summary>
    public string? ResourcePrefix { get; set; }
}

/// <summary>Configures common distributed defaults before registering queues or notifications.</summary>
public static class DistributedOptionsExtensions
{
    /// <summary>Sets shared defaults. Call before AddDistributedQueues/AddDistributedNotifications; feature overrides win.</summary>
    public static IMediatorBuilder ConfigureDistributed(this IMediatorBuilder builder, Action<DistributedOptions> configure)
    {
        if (builder.Services.Any(service => service.ServiceType == typeof(DistributedQueueOptions) || service.ServiceType == typeof(DistributedNotificationOptions)))
            throw new InvalidOperationException("Call ConfigureDistributed before registering distributed queues or notifications.");
        configure(builder.GetDistributedOptions());
        return builder;
    }

    /// <summary>Gets the shared naming defaults for mediator queues and notifications.</summary>
    public static DistributedOptions GetDistributedOptions(this IMediatorBuilder builder)
    {
        var existing = builder.Services.FirstOrDefault(service => service.ServiceType == typeof(DistributedOptions))?.ImplementationInstance as DistributedOptions;
        if (existing is not null)
            return existing;
        var options = new DistributedOptions();
        builder.Services.AddSingleton(options);
        return options;
    }
}
