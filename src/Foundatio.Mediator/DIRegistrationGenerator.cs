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
                string lifetimeMethod = "";
                if (String.Equals(handlerLifetime, "Transient", StringComparison.OrdinalIgnoreCase))
                    lifetimeMethod = "AddTransient";
                if (String.Equals(handlerLifetime, "Scoped", StringComparison.OrdinalIgnoreCase))
                    lifetimeMethod = "AddScoped";
                if (String.Equals(handlerLifetime, "Singleton", StringComparison.OrdinalIgnoreCase))
                    lifetimeMethod = "AddSingleton";

                if (!String.IsNullOrEmpty(lifetimeMethod))
                    source.AppendLine($"services.{lifetimeMethod}<{handler.FullName}>();");
            }

            if (handler.IsGenericHandlerClass)
            {
                // open generic registration
                if (handler is not { MessageGenericTypeDefinitionFullName: not null, GenericArity: > 0 })
                    continue;

                // Build unbound generic typeof expressions
                string wrapperTypeOf = handler.GenericArity switch
                {
                    1 => $"typeof({handlerClassName}<>)",
                    2 => $"typeof({handlerClassName}<,>)",
                    3 => $"typeof({handlerClassName}<,,>)",
                    4 => $"typeof({handlerClassName}<,,,>)",
                    5 => $"typeof({handlerClassName}<,,,,>)",
                    6 => $"typeof({handlerClassName}<,,,,,>)",
                    7 => $"typeof({handlerClassName}<,,,,,,>)",
                    8 => $"typeof({handlerClassName}<,,,,,,,>)",
                    9 => $"typeof({handlerClassName}<,,,,,,,,>)",
                    10 => $"typeof({handlerClassName}<,,,,,,,,,>)",
                    _ => $"typeof({handlerClassName}<>)" // fallback
                };
                string msgTypeOf = $"typeof({handler.MessageGenericTypeDefinitionFullName})";
                source.AppendLine($"// Open generic handler registration for {handler.MessageGenericTypeDefinitionFullName}");
                source.AppendLine($"services.AddSingleton(new OpenGenericHandlerDescriptor({msgTypeOf}, {wrapperTypeOf}, {handler.IsAsync.ToString().ToLower()}));");
            }
            else
            {
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
        }

        source.DecrementIndent();
        source.AppendLine("}");

        source.DecrementIndent();
        source.AppendLine("}");

        context.AddSource($"{className}.g.cs", source.ToString());
    }
}
