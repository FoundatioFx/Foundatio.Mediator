using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Foundatio.Mediator;

internal static class HandlerWrapperGenerator
{
    public static void GenerateHandlerWrappers(List<HandlerToGenerate> handlers, SourceProductionContext context)
    {
        foreach (var handler in handlers)
        {
            var wrapperClassName = GetWrapperClassName(handler);
            var source = GenerateHandlerWrapper(handler, wrapperClassName);
            var fileName = $"{wrapperClassName}.g.cs";
            context.AddSource(fileName, source);
        }
    }

    public static string GenerateHandlerWrapper(HandlerToGenerate handler, string wrapperClassName)
    {
        var source = new StringBuilder();
        
        source.AppendLine("using System;");
        source.AppendLine("using System.Threading;");
        source.AppendLine("using System.Threading.Tasks;");
        source.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        source.AppendLine();
        source.AppendLine("namespace Foundatio.Mediator");
        source.AppendLine("{");

        var hasReturnValue = handler.ReturnTypeName != "void" && 
                           handler.ReturnTypeName != "System.Threading.Tasks.Task" && 
                           !string.IsNullOrEmpty(handler.ReturnTypeName);
        
        // All wrappers now implement IHandler<TMessage> with generic HandleAsync<TResponse>
        source.AppendLine($"    internal class {wrapperClassName} : IHandler<{handler.MessageTypeName}>");
        source.AppendLine("    {");
        source.AppendLine("        private readonly IServiceProvider _serviceProvider;");
        source.AppendLine();
        source.AppendLine($"        public {wrapperClassName}(IServiceProvider serviceProvider)");
        source.AppendLine("        {");
        source.AppendLine("            _serviceProvider = serviceProvider;");
        source.AppendLine("        }");
        source.AppendLine();
        
        // Generate the generic HandleAsync<TResponse> method
        source.AppendLine($"        public async ValueTask<TResponse> HandleAsync<TResponse>({handler.MessageTypeName} message, CancellationToken cancellationToken)");
        source.AppendLine("        {");
        source.AppendLine($"            var handler = _serviceProvider.GetRequiredService<{handler.HandlerTypeName}>();");
        
        var methodCall = GenerateMethodCall(handler, "handler", "message", "cancellationToken");
        
        if (hasReturnValue)
        {
            // Handler returns a value - cast it to TResponse
            if (handler.IsAsync)
            {
                source.AppendLine($"            var result = await {methodCall};");
                source.AppendLine("            return (TResponse)(object)result!;");
            }
            else
            {
                source.AppendLine($"            var result = {methodCall};");
                source.AppendLine("            return (TResponse)(object)result!;");
            }
        }
        else
        {
            // Handler returns void - execute and return null
            if (handler.IsAsync)
            {
                source.AppendLine($"            await {methodCall};");
            }
            else
            {
                source.AppendLine($"            {methodCall};");
            }
            source.AppendLine("            return default(TResponse)!;");
        }
        
        source.AppendLine("        }");
        source.AppendLine("    }");

        source.AppendLine("}");

        return source.ToString();
    }

    public static string GetWrapperClassName(HandlerToGenerate handler)
    {
        // Create a unique wrapper class name based on handler type and method
        var handlerTypeName = handler.HandlerTypeName.Split('.').Last().Replace("<", "_").Replace(">", "_").Replace(",", "_");
        var methodName = handler.MethodName;
        return $"{handlerTypeName}_{methodName}_Wrapper";
    }

    private static string GenerateMethodCall(HandlerToGenerate handler, string handlerVariable, string messageVariable, string cancellationTokenVariable)
    {
        var parameters = new List<string>();
        
        foreach (var param in handler.Parameters)
        {
            if (param.IsMessage)
            {
                parameters.Add(messageVariable);
            }
            else if (param.IsCancellationToken)
            {
                parameters.Add(cancellationTokenVariable);
            }
            else
            {
                // This is a dependency that needs to be resolved from DI
                parameters.Add($"_serviceProvider.GetRequiredService<{param.TypeName}>()");
            }
        }
        
        var parameterList = string.Join(", ", parameters);
        return $"{handlerVariable}.{handler.MethodName}({parameterList})";
    }
}
