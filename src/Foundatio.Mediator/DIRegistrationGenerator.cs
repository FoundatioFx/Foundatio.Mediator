using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator;

internal static class DIRegistrationGenerator
{
    public static void Execute(SourceProductionContext context, List<HandlerInfo> handlers, Compilation compilation)
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
        source.AppendLine("[DebuggerStepThrough]");
        source.AppendLine("[DebuggerNonUserCode]");
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine($"public static class {className}");
        source.AppendLine("{");
        source.AppendLine("    [DebuggerStepThrough]");
        source.AppendLine("    public static void AddHandlers(this IServiceCollection services)");
        source.AppendLine("    {");
        source.AppendLine("        // Register HandlerRegistration instances keyed by message type name");
        source.AppendLine("        // Note: Handlers themselves are NOT auto-registered in DI");
        source.AppendLine("        // Users can register them manually if they want specific lifetimes");
        source.AppendLine();
        source.IncrementIndent().IncrementIndent();

        foreach (var handler in handlers)
        {
            string handlerClassName = HandlerGenerator.GetHandlerClassName(handler);

            // Use reflection FullName so nested types resolve with '+' and match runtime Type.FullName keys
            source.AppendLine($"services.AddKeyedSingleton<HandlerRegistration>(typeof({handler.MessageType.FullName}).FullName!,");
            source.AppendLine($"    new HandlerRegistration(");
            source.AppendLine($"        typeof({handler.MessageType.FullName}).FullName!,");

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
        }

        source.DecrementIndent().DecrementIndent();
        source.AppendLine("    }");
        source.AppendLine("}");

        context.AddSource($"{className}.g.cs", source.ToString());
    }
}
