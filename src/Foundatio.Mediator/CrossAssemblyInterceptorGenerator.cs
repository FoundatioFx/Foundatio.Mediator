using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

/// <summary>
/// Generates interceptor methods for call sites that invoke handlers in referenced assemblies.
/// This enables the interceptor pattern to work across project boundaries.
/// </summary>
internal static class CrossAssemblyInterceptorGenerator
{
    public static void Execute(
        SourceProductionContext context,
        List<HandlerInfo> crossAssemblyHandlers,
        ImmutableArray<CallSiteInfo> callSites,
        GeneratorConfiguration configuration)
    {
        if (!configuration.InterceptorsEnabled)
            return;

        if (crossAssemblyHandlers.Count == 0)
            return;

        // Build a lookup of cross-assembly handlers by message type
        // Note: Multiple handlers for the same message type may exist across referenced assemblies.
        // For InvokeAsync, we take the first one found (similar to how local handlers work).
        var handlersByMessageType = crossAssemblyHandlers
            .GroupBy(h => h.MessageType.FullName)
            .ToDictionary(g => g.Key, g => g.First());

        // Find call sites that have handlers in referenced assemblies (not in this assembly)
        var crossAssemblyCallSites = callSites
            .Where(cs => !cs.IsPublish && handlersByMessageType.ContainsKey(cs.MessageType.FullName))
            .GroupBy(cs => cs.MessageType.FullName)
            .ToList();

        if (crossAssemblyCallSites.Count == 0)
            return;

        const string hintName = "_CrossAssemblyInterceptors.g.cs";
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, hintName);

        source.AppendLines("""
            using System;
            using System.Diagnostics;
            using System.Diagnostics.CodeAnalysis;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;

            namespace Foundatio.Mediator.Generated;
            """);

        source.AppendLine();
        source.AddGeneratedCodeAttribute();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine("internal static class CrossAssemblyInterceptors");
        source.AppendLine("{");
        source.IncrementIndent();

        int messageTypeCounter = 0;
        foreach (var group in crossAssemblyCallSites)
        {
            var messageTypeName = group.Key;
            var handler = handlersByMessageType[messageTypeName];
            var callSitesForMessage = group.ToList();

            // Group call sites by method name, response type, IRequest overload usage, and whether it's async
            var callSiteGroups = callSitesForMessage
                .GroupBy(cs => new { cs.MethodName, cs.ResponseType, cs.UsesIRequestOverload, cs.IsAsyncMethod })
                .ToList();

            int methodCounter = 0;
            foreach (var csGroup in callSiteGroups)
            {
                GenerateInterceptorMethod(source, handler, csGroup.Key.MethodName, csGroup.Key.ResponseType, csGroup.Key.UsesIRequestOverload, csGroup.Key.IsAsyncMethod, csGroup.ToList(), messageTypeCounter, methodCounter++);
                source.AppendLine();
            }

            messageTypeCounter++;
        }

        source.DecrementIndent();
        source.AppendLine("}");

        context.AddSource(hintName, source.ToString());
    }

    private static void GenerateInterceptorMethod(
        IndentedStringBuilder source,
        HandlerInfo handler,
        string methodName,
        TypeSymbolInfo responseType,
        bool usesIRequestOverload,
        bool isAsyncMethod,
        List<CallSiteInfo> callSites,
        int messageTypeIndex,
        int methodIndex)
    {
        // Get the generated wrapper class name for this handler
        string wrapperClassName = $"global::{HandlerGenerator.GetHandlerFullName(handler)}";

        string interceptorMethod = $"Intercept{handler.MessageType.Identifier}_{methodName}{methodIndex}";

        // Add InterceptsLocation attributes for each call site
        foreach (var callSite in callSites)
        {
            source.AppendLine($"[InterceptsLocation({callSite.Location.Version}, \"{callSite.Location.Data}\")] // {callSite.Location.DisplayLocation}");
        }

        var returnInfo = InterceptorCodeEmitter.ComputeReturnInfo(handler, responseType, isAsyncMethod);

        // Use IRequest<T> parameter type when the call site uses that overload
        string messageParameterType = usesIRequestOverload
            ? $"Foundatio.Mediator.IRequest<{responseType.UnwrappedFullName}>"
            : "object";
        string parameters = $"this Foundatio.Mediator.IMediator mediator, {messageParameterType} message, System.Threading.CancellationToken cancellationToken = default";

        source.AppendLine($"public static {returnInfo.AsyncModifier}{returnInfo.ReturnType} {interceptorMethod}({parameters})");
        source.AppendLine("{");
        source.IncrementIndent();

        source.AppendLine($"var typedMessage = (global::{handler.MessageType.FullName})message;");

        // Determine which method to call based on response type
        string targetMethod = InterceptorCodeEmitter.GetTargetMethodName(handler, responseType);

        // Emit the interceptor method body
        InterceptorCodeEmitter.EmitInterceptorMethodBody(source, handler, wrapperClassName, targetMethod, responseType, returnInfo);

        source.DecrementIndent();
        source.AppendLine("}");
    }
}
