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

        // Get the notification publisher from the calling assembly (where AddMediator is called)
        // If the calling assembly isn't a Foundatio module, fall back to the first config assembly
        var callingAssembly = Assembly.GetCallingAssembly();
        INotificationPublisher? publisher = GetNotificationPublisherFromAssembly(callingAssembly);

        foreach (var assembly in configuration.Assemblies!)
        {
            if (!IsAssemblyMarkedWithFoundatioModule(assembly))
                continue;

            var moduleType = assembly.GetTypes().FirstOrDefault(t =>
                t.IsClass &&
                t.IsAbstract &&
                t.IsSealed &&
                t.Name.EndsWith("_MediatorHandlers"));

            if (moduleType == null)
                continue;

            // If calling assembly wasn't a Foundatio module, use the first config assembly's publisher
            if (publisher == null)
            {
                var publisherProperty = moduleType.GetProperty("NotificationPublisher", BindingFlags.Public | BindingFlags.Static);
                publisher = publisherProperty?.GetValue(null) as INotificationPublisher;
            }

            // Call AddHandlers to register handlers
            var method = moduleType.GetMethod("AddHandlers", BindingFlags.Public | BindingFlags.Static);
            method?.Invoke(null, [services]);
        }

        // Set the notification publisher: calling assembly > first config assembly > default
        Mediator.NotificationPublisher = publisher ?? new ForeachAwaitPublisher();

        services.Add(ServiceDescriptor.Describe(typeof(IMediator), typeof(Mediator), configuration.MediatorLifetime));

        return services;
    }

    private static INotificationPublisher? GetNotificationPublisherFromAssembly(Assembly assembly)
    {
        if (!IsAssemblyMarkedWithFoundatioModule(assembly))
            return null;

        var moduleType = assembly.GetTypes().FirstOrDefault(t =>
            t.IsClass &&
            t.IsAbstract &&
            t.IsSealed &&
            t.Name.EndsWith("_MediatorHandlers"));

        if (moduleType == null)
            return null;

        var publisherProperty = moduleType.GetProperty("NotificationPublisher", BindingFlags.Public | BindingFlags.Static);
        return publisherProperty?.GetValue(null) as INotificationPublisher;
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

    private static bool IsAssemblyMarkedWithFoundatioModule(Assembly assembly)
    {
        return assembly.GetCustomAttributes(typeof(FoundatioModuleAttribute), false).Any();
    }
}

public static class MediatorServiceCollectionExtensions
{
    public static IServiceCollection AddHandler(this IServiceCollection services, HandlerRegistration registration)
    {
        services.AddKeyedSingleton(registration.MessageTypeName, registration);
        services.AddSingleton(registration);
        return services;
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

    public MediatorConfiguration Build() => _configuration;
}
