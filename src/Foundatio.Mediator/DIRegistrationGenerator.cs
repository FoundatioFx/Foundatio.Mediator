using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator;

internal static class DIRegistrationGenerator
{
    public static void Execute(SourceProductionContext context, List<HandlerInfo> handlers, Compilation compilation, string handlerLifetime)
    {
        var assemblyName = compilation.AssemblyName?.ToIdentifier() ?? Guid.NewGuid().ToString("N").Substring(0, 10);
        var className = $"{assemblyName}_MediatorHandlers";

        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader();

        source.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        source.AppendLine("using System;");
        source.AppendLine("using System.Diagnostics;");
        source.AppendLine("using System.Diagnostics.CodeAnalysis;");
        source.AppendLine("using System.Threading;");
        source.AppendLine("using System.Threading.Tasks;");
        source.AppendLine();
        source.AppendLine("[assembly: Foundatio.Mediator.FoundatioHandlerModule]");
        source.AppendLine();
        source.AppendLine("namespace Foundatio.Mediator;");
        source.AppendLine();
        source.AddGeneratedCodeAttribute();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine($"public static class {className}");
        source.AppendLine("{");
        source.AppendLine("    public static void AddHandlers(this IServiceCollection services)");
        source.AppendLine("    {");
        source.AppendLine("        // Register HandlerRegistration instances keyed by message type name");
        source.AppendLine("        // Optionally register handler classes into DI based on MediatorHandlerLifetime setting");
        source.AppendLine();
        source.IncrementIndent().IncrementIndent();

        bool registerHandlers = !string.Equals(handlerLifetime, "None", StringComparison.OrdinalIgnoreCase);

        foreach (var handler in handlers)
        {
            string handlerClassName = HandlerGenerator.GetHandlerClassName(handler);

            // Register handler in DI for non-static handler classes when lifetime != Singleton
            if (registerHandlers && !handler.IsStatic)
            {
                var lifetimeMethod = "";
                if (string.Equals(handlerLifetime, "Transient", StringComparison.OrdinalIgnoreCase))
                    lifetimeMethod = "AddTransient";
                if (string.Equals(handlerLifetime, "Scoped", StringComparison.OrdinalIgnoreCase))
                    lifetimeMethod = "AddScoped";
                if (string.Equals(handlerLifetime, "Singleton", StringComparison.OrdinalIgnoreCase))
                    lifetimeMethod = "AddSingleton";

                if (!String.IsNullOrEmpty(lifetimeMethod))
                    source.AppendLine($"services.{lifetimeMethod}<{handler.FullName}>();");
            }

            // Use reflection FullName so nested types resolve with '+' and match runtime Type.FullName keys
            source.AppendLine($"services.AddHandler(new HandlerRegistration(");
            source.AppendLine($"        MessageTypeKey.Get(typeof({handler.MessageType.FullName})),");
            source.AppendLine($"        \"{handlerClassName}\",");

            if (handler.IsAsync)
            {
                source.AppendLine($"        {handlerClassName}.UntypedHandleAsync,");
                source.AppendLine($"        null,");
            }
            else
            {
                source.AppendLine($"        (mediator, message, cancellationToken, responseType) => new ValueTask<object?>({handlerClassName}.UntypedHandle(mediator, message, cancellationToken, responseType)),");
                source.AppendLine($"        {handlerClassName}.UntypedHandle,");
            }

            source.AppendLine($"        {handler.IsAsync.ToString().ToLower()}));");

            source.AppendLine();
        }

        source.DecrementIndent().DecrementIndent();
        source.AppendLine("    }");
        source.AppendLine("}");

        context.AddSource($"{className}.g.cs", source.ToString());
    }
}
