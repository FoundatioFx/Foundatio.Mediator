using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator;

public static class MediatorExtensions
{
    /// <summary>
    /// Adds Foundatio.Mediator to the service collection.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration">Optional configuration for the mediator.</param>
    /// <returns>The updated service collection with Foundatio.Mediator registered.</returns>
    public static IServiceCollection AddMediator(this IServiceCollection services, MediatorConfiguration? configuration = null)
    {
        configuration ??= new MediatorConfiguration();

        if (configuration.Assemblies == null)
        {
            configuration.Assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !a.FullName.StartsWith("System.")).ToList();
        }

        services.Add(ServiceDescriptor.Describe(typeof(IMediator), typeof(Mediator), configuration.MediatorLifetime));

        foreach (var assembly in configuration.Assemblies!)
        {
            if (!IsAssemblyMarkedWithFoundatioHandlerModule(assembly))
                continue;

            var moduleType = assembly.GetTypes().FirstOrDefault(t =>
                t.IsClass &&
                t.IsAbstract &&
                t.IsSealed &&
                t.Name.EndsWith("_MediatorHandlers"));

            var method = moduleType?.GetMethod("AddHandlers", BindingFlags.Public | BindingFlags.Static);

            if (method != null)
            {
                method.Invoke(null, [services]);
            }
        }

        return services;
    }

    /// <summary>
    /// Adds Foundatio.Mediator to the service collection with a configuration builder.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static IServiceCollection AddMediator(this IServiceCollection services, Action<MediatorConfigurationBuilder> builder)
    {
        var configurationBuilder = new MediatorConfigurationBuilder();
        builder(configurationBuilder);
        return services.AddMediator(configurationBuilder.Build());
    }

    /// <summary>
    /// Adds a handler registration to the service collection.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="registration"></param>
    /// <returns></returns>
    public static IServiceCollection AddHandler(this IServiceCollection services, HandlerRegistration registration)
    {
        services.AddKeyedSingleton(registration.MessageTypeName, registration);
        services.AddSingleton(registration);
        return services;
    }

    private static bool IsAssemblyMarkedWithFoundatioHandlerModule(Assembly assembly)
    {
        return assembly.GetCustomAttributes(typeof(FoundatioHandlerModuleAttribute), false).Any();
    }
}

public class MediatorConfigurationBuilder
{
    private readonly MediatorConfiguration _configuration = new MediatorConfiguration();

    /// <summary>
    /// Adds the specified assemblies to the mediator configuration.
    /// </summary>
    /// <param name="assemblies"></param>
    /// <returns></returns>
    public MediatorConfigurationBuilder AddAssembly(params Assembly[] assemblies)
    {
        _configuration.Assemblies ??= new List<Assembly>();
        _configuration.Assemblies.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Adds the assembly containing the specified type to the mediator configuration.
    /// </summary>
    /// <typeparam name="T">The type whose assembly should be added.</typeparam>
    /// <returns></returns>
    public MediatorConfigurationBuilder AddAssembly<T>()
    {
        var assembly = typeof(T).Assembly;
        return AddAssembly(assembly);
    }

    /// <summary>
    /// Sets the lifetime of the mediator.
    /// </summary>
    /// <param name="lifetime"></param>
    /// <returns></returns>
    public MediatorConfigurationBuilder SetMediatorLifetime(ServiceLifetime lifetime)
    {
        _configuration.MediatorLifetime = lifetime;
        return this;
    }

    /// <summary>
    /// Sets the publisher for the mediator.
    /// </summary>
    /// <param name="publisher"></param>
    /// <returns></returns>
    public MediatorConfigurationBuilder SetPublisher(INotificationPublisher publisher)
    {
        _configuration.NotificationPublisher = publisher;
        return this;
    }

    /// <summary>
    /// Uses the ForeachAwaitPublisher for the mediator.
    /// </summary>
    /// <returns></returns>
    public MediatorConfigurationBuilder UseForeachAwaitPublisher()
    {
        _configuration.NotificationPublisher = new ForeachAwaitPublisher();
        return this;
    }

    /// <summary>
    /// Uses the TaskWhenAllPublisher for the mediator.
    /// </summary>
    /// <returns></returns>
    public MediatorConfigurationBuilder TaskWhenAllPublisher()
    {
        _configuration.NotificationPublisher = new TaskWhenAllPublisher();
        return this;
    }

    public MediatorConfiguration Build() => _configuration;
}
