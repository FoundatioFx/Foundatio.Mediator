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

    private static bool IsAssemblyMarkedWithFoundatioHandlerModule(Assembly assembly)
    {
        return assembly.GetCustomAttributes(typeof(FoundatioHandlerModuleAttribute), false).Any();
    }
}

public class MediatorConfigurationBuilder
{
    private readonly MediatorConfiguration _configuration = new MediatorConfiguration();

    public MediatorConfigurationBuilder AddAssembly(params Assembly[] assemblies)
    {
        _configuration.Assemblies ??= new List<Assembly>();
        _configuration.Assemblies.AddRange(assemblies);
        return this;
    }

    public MediatorConfigurationBuilder AddAssembly<T>()
    {
        var assembly = typeof(T).Assembly;
        return AddAssembly(assembly);
    }

    public MediatorConfiguration Build() => _configuration;
}
