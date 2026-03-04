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
    /// By default, the mediator lifetime is auto-detected: <b>Scoped</b> in ASP.NET Core applications
    /// (where <c>IWebHostEnvironment</c> is registered) and <b>Singleton</b> otherwise.
    /// This ensures scoped services like <c>DbContext</c> are resolved from the correct DI scope
    /// in web applications without any extra configuration.
    /// </para>
    /// <para>
    /// You can override this by setting the lifetime explicitly:
    /// </para>
    /// <code>
    /// services.AddMediator(b => b.SetMediatorLifetime(ServiceLifetime.Singleton));
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

        if (options.Assemblies.Count == 0)
        {
            options.Assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.FullName?.StartsWith("System.") != true).ToList();
        }

        var registry = new HandlerRegistry();

        var callingAssembly = Assembly.GetCallingAssembly();
        NotificationPublishStrategy? strategy = GetPublishStrategyFromAssembly(callingAssembly);

        foreach (var assembly in options.Assemblies)
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

            if (strategy == null)
            {
                var strategyProperty = moduleType.GetProperty("PublishStrategy", BindingFlags.Public | BindingFlags.Static);
                if (strategyProperty?.GetValue(null) is NotificationPublishStrategy s)
                    strategy = s;
            }

            var method = moduleType.GetMethod("AddHandlers", BindingFlags.Public | BindingFlags.Static);
            method?.Invoke(null, [services, registry]);
        }

        registry.Freeze();
        services.AddSingleton(registry);

        if (options.LogHandlers)
            registry.ShowRegisteredHandlers();

        if (options.LogMiddleware)
            registry.ShowRegisteredMiddleware();

        var resolvedStrategy = strategy ?? NotificationPublishStrategy.ForeachAwait;
        services.TryAddSingleton<INotificationPublisher>(sp => CreatePublisher(resolvedStrategy, sp));

        var lifetime = options.MediatorLifetime ?? DetectDefaultLifetime(services);
        services.Add(ServiceDescriptor.Describe(typeof(IMediator), typeof(Mediator), lifetime));

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

    private static NotificationPublishStrategy? GetPublishStrategyFromAssembly(Assembly assembly)
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

        var strategyProperty = moduleType.GetProperty("PublishStrategy", BindingFlags.Public | BindingFlags.Static);
        return strategyProperty?.GetValue(null) as NotificationPublishStrategy?;
    }

    private static INotificationPublisher CreatePublisher(NotificationPublishStrategy strategy, IServiceProvider sp)
    {
        return strategy switch
        {
            NotificationPublishStrategy.TaskWhenAll => new TaskWhenAllPublisher(),
            NotificationPublishStrategy.FireAndForget => new FireAndForgetPublisher(sp.GetService<ILogger<FireAndForgetPublisher>>()),
            _ => new ForeachAwaitPublisher()
        };
    }

    /// <summary>
    /// Auto-detects the appropriate mediator lifetime based on the hosting environment.
    /// Returns Scoped for ASP.NET Core applications (IWebHostEnvironment is registered),
    /// Singleton otherwise.
    /// </summary>
    private static ServiceLifetime DetectDefaultLifetime(IServiceCollection services)
    {
        // IWebHostEnvironment is always registered by WebApplicationBuilder / WebHost.
        // Its presence reliably indicates an ASP.NET Core web application where the
        // mediator should be Scoped so that scoped services (DbContext, etc.) are
        // resolved from the correct per-request scope.
        bool isWebApp = services.Any(sd =>
            sd.ServiceType.FullName == "Microsoft.AspNetCore.Hosting.IWebHostEnvironment");

        return isWebApp ? ServiceLifetime.Scoped : ServiceLifetime.Singleton;
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
    /// Enables logging of all registered handlers at startup.
    /// </summary>
    public MediatorOptionsBuilder LogHandlers(bool log = true)
    {
        _options.LogHandlers = log;
        return this;
    }

    /// <summary>
    /// Enables logging of the middleware pipeline at startup.
    /// </summary>
    public MediatorOptionsBuilder LogMiddleware(bool log = true)
    {
        _options.LogMiddleware = log;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="MediatorOptions"/>.
    /// </summary>
    public MediatorOptions Build() => _options;
}

