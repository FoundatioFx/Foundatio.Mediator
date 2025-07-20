using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Foundatio.Mediator;

internal static class DIRegistrationGenerator
{
    public static string GenerateDIRegistration(List<HandlerToGenerate> handlers)
    {
        var source = new StringBuilder();
        
        source.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        source.AppendLine();
        source.AppendLine("namespace Foundatio.Mediator");
        source.AppendLine("{");
        source.AppendLine("    public static partial class ServiceCollectionExtensions");
        source.AppendLine("    {");
        source.AppendLine("        public static IServiceCollection AddMediator(this IServiceCollection services)");
        source.AppendLine("        {");
        source.AppendLine("            services.AddSingleton<IMediator, Mediator>();");
        source.AppendLine();
        source.AppendLine("            // Register all discovered handlers");
        
        var uniqueHandlers = handlers.Select(h => h.HandlerTypeName).Distinct();
        foreach (var handlerType in uniqueHandlers)
        {
            source.AppendLine($"            services.AddScoped<{handlerType}>();");
        }
        
        source.AppendLine();
        source.AppendLine("            // Register handler registrations containing wrapper handlers with metadata");
        
        foreach (var handler in handlers)
        {
            var wrapperClassName = HandlerWrapperGenerator.GetWrapperClassName(handler);
            var isAsync = handler.IsAsync ? "true" : "false";
            
            // First register the wrapper itself
            source.AppendLine($"            services.AddScoped<{wrapperClassName}>();");
            
            // Then register the HandlerRegistration (only using TMessage now, no TResponse)
            source.AppendLine($"            services.AddScoped<HandlerRegistration<{handler.MessageTypeName}>>(sp =>");
            source.AppendLine($"                new HandlerRegistration<{handler.MessageTypeName}>(");
            source.AppendLine($"                    sp.GetRequiredService<{wrapperClassName}>(), {isAsync}));");
        }
        
        source.AppendLine();
        source.AppendLine("            return services;");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }
}
