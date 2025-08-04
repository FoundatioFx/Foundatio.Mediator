using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator;

public static class MediatorExtensions
{
    /// <summary>
    /// Adds Foundatio.Mediator to the service collection.
    /// </summary>
    /// <param name="services"></param>
    /// <returns>The updated service collection with Foundatio.Mediator registered.</returns>
    public static IServiceCollection AddMediator(this IServiceCollection services)
    {
        services.AddSingleton<IMediator, Mediator>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
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

    private static bool IsAssemblyMarkedWithFoundatioHandlerModule(Assembly assembly)
    {
        return assembly.GetCustomAttributes(typeof(FoundatioHandlerModuleAttribute), false).Any();
    }
}
