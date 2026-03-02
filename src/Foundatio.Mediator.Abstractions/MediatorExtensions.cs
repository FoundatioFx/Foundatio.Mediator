using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
    /// <param name="options">Optional configuration options for the mediator.</param>
    /// <returns>The updated service collection with Foundatio.Mediator registered.</returns>
    public static IServiceCollection AddMediator(this IServiceCollection services, MediatorOptions? options = null)
    {
        if (services.Any(sd => sd.ServiceType == typeof(IMediator)))
            return services;

        options ??= new MediatorOptions();

        if (options.Assemblies == null)
        {
            options.Assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.FullName?.StartsWith("System.") != true).ToList();
        }

        var registry = new HandlerRegistry();

        var callingAssembly = Assembly.GetCallingAssembly();
        INotificationPublisher? publisher = GetNotificationPublisherFromAssembly(callingAssembly);

        foreach (var assembly in options.Assemblies!)
        {
            if (!IsAssemblyMarkedWithFoundatioModule(assembly))
                continue;

            var moduleType = GetLoadableTypes(assembly).FirstOrDefault(t =>
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

        services.TryAddSingleton<INotificationPublisher>(sp =>
        {
            if (publisher is FireAndForgetPublisher)
                return new FireAndForgetPublisher(sp.GetService<ILogger<FireAndForgetPublisher>>());

            return publisher ?? new ForeachAwaitPublisher();
        });

        services.Add(ServiceDescriptor.Describe(typeof(IMediator), typeof(Mediator), options.MediatorLifetime));

        services.TryAddSingleton<IHandlerAuthorizationService, DefaultHandlerAuthorizationService>();
        services.TryAddSingleton<IAuthorizationContextProvider, DefaultAuthorizationContextProvider>();

        return services;
    }

    /// <summary>
    /// Adds Foundatio.Mediator to the service collection with a configuration builder.
    /// </summary>
    public static IServiceCollection AddMediator(this IServiceCollection services, Action<MediatorOptionsBuilder> builder)
    {
        var optionsBuilder = new MediatorOptionsBuilder();
        builder(optionsBuilder);
        return services.AddMediator(optionsBuilder.Build());
    }

    private static INotificationPublisher? GetNotificationPublisherFromAssembly(Assembly assembly)
    {
        if (!IsAssemblyMarkedWithFoundatioModule(assembly))
            return null;

        var moduleType = GetLoadableTypes(assembly).FirstOrDefault(t =>
            t.IsClass &&
            t.IsAbstract &&
            t.IsSealed &&
            t.Name.EndsWith("_MediatorHandlers"));

        if (moduleType == null)
            return null;

        var publisherProperty = moduleType.GetProperty("NotificationPublisher", BindingFlags.Public | BindingFlags.Static);
        return publisherProperty?.GetValue(null) as INotificationPublisher;
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).ToArray()!;
        }
    }

    private static bool IsAssemblyMarkedWithFoundatioModule(Assembly assembly)
    {
        return assembly.GetCustomAttributes(typeof(FoundatioModuleAttribute), false).Any();
    }
}

/// <summary>
/// Builder for configuring <see cref="MediatorOptions"/>.
/// </summary>
public sealed class MediatorOptionsBuilder
{
    private readonly MediatorOptions _options = new MediatorOptions();

    /// <summary>
    /// Adds the specified assemblies to the mediator configuration.
    /// </summary>
    public MediatorOptionsBuilder AddAssembly(params Assembly[] assemblies)
    {
        _options.Assemblies ??= new List<Assembly>();
        _options.Assemblies.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Adds the assembly containing the specified type to the mediator configuration.
    /// </summary>
    public MediatorOptionsBuilder AddAssembly<T>()
    {
        var assembly = typeof(T).Assembly;
        return AddAssembly(assembly);
    }

    /// <summary>
    /// Sets the lifetime of the mediator.
    /// </summary>
    public MediatorOptionsBuilder SetMediatorLifetime(ServiceLifetime lifetime)
    {
        _options.MediatorLifetime = lifetime;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="MediatorOptions"/>.
    /// </summary>
    public MediatorOptions Build() => _options;
}

