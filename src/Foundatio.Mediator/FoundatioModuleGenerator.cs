using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class FoundatioModuleGenerator
{
    /// <summary>
    /// Generates the [assembly: FoundatioModule] attribute to mark assemblies that contain handlers or middleware.
    /// This attribute is used by MetadataMiddlewareScanner to discover middleware in referenced assemblies.
    /// Also generates the AddHandlers extension method for DI registration.
    /// </summary>
    public static void Execute(SourceProductionContext context, Compilation compilation, List<HandlerInfo> handlers, string handlerLifetime)
    {
        var assemblyName = compilation.AssemblyName?.ToIdentifier() ?? Guid.NewGuid().ToString("N").Substring(0, 10);
        var className = $"{assemblyName}_MediatorHandlers";

        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader();

        if (handlers.Count > 0)
        {
            source.AppendLine();
            source.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            source.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
            source.AppendLine("using System;");
            source.AppendLine("using System.Diagnostics;");
            source.AppendLine("using System.Diagnostics.CodeAnalysis;");
            source.AppendLine("using System.Threading;");
            source.AppendLine("using System.Threading.Tasks;");
        }

        source.AppendLine();
        source.AppendLine("[assembly: Foundatio.Mediator.FoundatioModule]");
        source.AppendLine();

        if (handlers.Count > 0)
        {
            source.AppendLine("namespace Foundatio.Mediator;");
            source.AppendLine();
            source.AddGeneratedCodeAttribute();
            source.AppendLine("[ExcludeFromCodeCoverage]");
            source.AppendLine($"public static class {className}");
            source.AppendLine("{");
            source.AppendLine("    public static void AddHandlers(this IServiceCollection services)");
            source.AppendLine("    {");
            source.AppendLine("        // Register HandlerRegistration instances keyed by message type name");
            source.AppendLine();
            source.IncrementIndent().IncrementIndent();

            string lifetimeMethod;
            if (String.Equals(handlerLifetime, "Transient", StringComparison.OrdinalIgnoreCase))
                lifetimeMethod = "TryAddTransient";
            else if (String.Equals(handlerLifetime, "Scoped", StringComparison.OrdinalIgnoreCase))
                lifetimeMethod = "TryAddScoped";
            else
                lifetimeMethod = "TryAddSingleton";

            foreach (var handler in handlers)
            {
                string handlerClassName = HandlerGenerator.GetHandlerClassName(handler);

                if (handler is { IsStatic: false, IsGenericHandlerClass: false })
                {
                    source.AppendLine($"services.{lifetimeMethod}<{handler.FullName}>();");
                }

                if (handler.IsGenericHandlerClass)
                {
                    if (handler is not { MessageGenericTypeDefinitionFullName: not null, GenericArity: > 0 })
                        continue;

                    string genericArity = handler.GenericArity switch
                    {
                        1 => "<>",
                        2 => "<,>",
                        3 => "<,,>",
                        4 => "<,,,>",
                        5 => "<,,,,>",
                        6 => "<,,,,,>",
                        7 => "<,,,,,,>",
                        8 => "<,,,,,,,>",
                        9 => "<,,,,,,,,>",
                        10 => "<,,,,,,,,,>",
                        _ => "<>)" // fallback
                    };

                    string wrapperTypeOf = $"typeof({handlerClassName}{genericArity})";
                    string msgTypeOf = $"typeof({handler.MessageGenericTypeDefinitionFullName})";
                    if (!handler.IsStatic)
                    {
                        string handlerFullName = handler.FullName;
                        int index = handlerFullName.IndexOf('<');
                        if (index > 0)
                            handlerFullName = handlerFullName.Substring(0, index);
                        source.AppendLine($"services.{lifetimeMethod}(typeof({handlerFullName}{genericArity}));");

                    }

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
        }

        context.AddSource("_FoundatioModule.cs", source.ToString());
    }
}
