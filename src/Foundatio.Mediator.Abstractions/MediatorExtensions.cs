using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Foundatio.Mediator;

public static class MediatorExtensions
{
    /// <summary>
    /// Adds Foundatio.Mediator to the service collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default, the mediator is registered as a singleton. If your handlers use scoped or transient
    /// services (like DbContext), you should register the mediator as scoped to ensure services are
    /// resolved from the correct DI scope:
    /// </para>
    /// <code>
    /// services.AddMediator(b => b.SetMediatorLifetime(ServiceLifetime.Scoped));
    /// </code>
    /// </remarks>
    /// <param name="services">The service collection to add the mediator to.</param>
    /// <param name="configuration">Optional configuration for the mediator.</param>
    /// <returns>The updated service collection with Foundatio.Mediator registered.</returns>
    public static IServiceCollection AddMediator(this IServiceCollection services, MediatorConfiguration? configuration = null)
    {
        if (services.Any(sd => sd.ServiceType == typeof(IMediator)))
            return services;

        configuration ??= new MediatorConfiguration();

        if (configuration.Assemblies == null)
        {
            configuration.Assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.FullName?.StartsWith("System.") != true).ToList();
        }

        var registry = new HandlerRegistry();

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

            if (publisher == null)
            {
                var publisherProperty = moduleType.GetProperty("NotificationPublisher", BindingFlags.Public | BindingFlags.Static);
                publisher = publisherProperty?.GetValue(null) as INotificationPublisher;
            }

            var method = moduleType.GetMethod("AddHandlers", BindingFlags.Public | BindingFlags.Static);
            method?.Invoke(null, [services, registry]);
        }

        registry.Freeze();
        services.AddSingleton(registry);

        services.TryAddSingleton<INotificationPublisher>(publisher ?? new ForeachAwaitPublisher());

        services.Add(ServiceDescriptor.Describe(typeof(IMediator), typeof(Mediator), configuration.MediatorLifetime));

        services.TryAddSingleton<IHandlerAuthorizationService, DefaultHandlerAuthorizationService>();
        services.TryAddSingleton<IAuthorizationContextProvider, DefaultAuthorizationContextProvider>();

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

public sealed class MediatorConfigurationBuilder
{
    private readonly MediatorConfiguration _configuration = new MediatorConfiguration();

    /// <summary>
    /// Adds the specified assemblies to the mediator configuration.
    /// </summary>
    public MediatorConfigurationBuilder AddAssembly(params Assembly[] assemblies)
    {
        _configuration.Assemblies ??= new List<Assembly>();
        _configuration.Assemblies.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Adds the assembly containing the specified type to the mediator configuration.
    /// </summary>
    public MediatorConfigurationBuilder AddAssembly<T>()
    {
        var assembly = typeof(T).Assembly;
        return AddAssembly(assembly);
    }

    /// <summary>
    /// Sets the lifetime of the mediator.
    /// </summary>
    public MediatorConfigurationBuilder SetMediatorLifetime(ServiceLifetime lifetime)
    {
        _configuration.MediatorLifetime = lifetime;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="MediatorConfiguration"/>.
    /// </summary>
    public MediatorConfiguration Build() => _configuration;
}
