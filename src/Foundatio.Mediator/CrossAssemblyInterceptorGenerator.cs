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

            // Group call sites by method name, response type, and IRequest overload usage
            var callSiteGroups = callSitesForMessage
                .GroupBy(cs => new { cs.MethodName, cs.ResponseType, cs.UsesIRequestOverload })
                .ToList();

            int methodCounter = 0;
            foreach (var csGroup in callSiteGroups)
            {
                GenerateInterceptorMethod(source, handler, csGroup.Key.MethodName, csGroup.Key.ResponseType, csGroup.Key.UsesIRequestOverload, csGroup.ToList(), messageTypeCounter, methodCounter++);
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
        List<CallSiteInfo> callSites,
        int messageTypeIndex,
        int methodIndex)
    {
        // Get the generated wrapper class name for this handler
        string wrapperClassName = $"global::{HandlerGenerator.GetHandlerFullName(handler)}";
        string handlerMethodName = HandlerGenerator.GetHandlerMethodName(handler);

        string interceptorMethod = $"Intercept{handler.MessageType.Identifier}_{methodName}{methodIndex}";
        bool methodIsAsync = methodName.EndsWith("Async") || handler.IsAsync;

        // Add InterceptsLocation attributes for each call site
        foreach (var callSite in callSites)
        {
            source.AppendLine($"[InterceptsLocation({callSite.Location.Version}, \"{callSite.Location.Data}\")] // {callSite.Location.DisplayLocation}");
        }

        // Need async for:
        // - Tuple returns (await PublishAsync for cascading messages)
        // - Async handlers (await using for scope management)
        bool needsAsyncModifier = handler.ReturnType.IsTuple || handler.IsAsync;
        string asyncModifier = needsAsyncModifier ? "async " : "";
        string returnType = methodIsAsync ? $"System.Threading.Tasks.ValueTask<{responseType.UnwrappedFullName}>" : responseType.UnwrappedFullName;
        if (responseType.IsVoid)
            returnType = methodIsAsync ? "System.Threading.Tasks.ValueTask" : "void";

        // Use IRequest<T> parameter type when the call site uses that overload
        string messageParameterType = usesIRequestOverload
            ? $"Foundatio.Mediator.IRequest<{responseType.UnwrappedFullName}>"
            : "object";
        string parameters = $"this Foundatio.Mediator.IMediator mediator, {messageParameterType} message, System.Threading.CancellationToken cancellationToken = default";

        source.AppendLine($"public static {asyncModifier}{returnType} {interceptorMethod}({parameters})");
        source.AppendLine("{");
        source.IncrementIndent();

        if (handler.IsAsync)
        {
            source.AppendLine("await using var scopedMediator = ScopedMediator.GetOrCreateAsyncScope(mediator);");
        }
        else
        {
            source.AppendLine("using var scopedMediator = ScopedMediator.GetOrCreateScope(mediator);");
        }

        source.AppendLine($"var typedMessage = (global::{handler.MessageType.FullName})message;");

        if (handler.ReturnType.IsTuple)
        {
            // Handle tuple return types (cascading messages)
            string awaitKeyword = handler.IsAsync ? "await " : "";
            source.AppendLine($"var result = {awaitKeyword}{wrapperClassName}.{handlerMethodName}(scopedMediator.Services, typedMessage, cancellationToken);");
            source.AppendLine();

            // Find the return item and publish items
            var returnItem = handler.ReturnType.TupleItems.FirstOrDefault(i => i.TypeFullName == responseType.FullName);
            if (returnItem == default)
            {
                returnItem = handler.ReturnType.TupleItems.First();
            }
            var publishItems = handler.ReturnType.TupleItems.Where(i => i.Name != returnItem.Name).ToList();

            foreach (var publishItem in publishItems)
            {
                source.AppendLineIf($"if (result.{publishItem.Name} != null)", publishItem.IsNullable);
                source.AppendIf("    ", publishItem.IsNullable).AppendLine($"await scopedMediator.PublishAsync(result.{publishItem.Name}, cancellationToken);");
            }

            if (publishItems.Count > 0)
                source.AppendLine();

            source.AppendLine($"return result.{returnItem.Name};");
        }
        else
        {
            // Simple return type
            bool needsValueTaskWrap = methodIsAsync && !handler.IsAsync;

            if (responseType.IsVoid)
            {
                if (handler.IsAsync)
                {
                    source.AppendLine($"await {wrapperClassName}.{handlerMethodName}(scopedMediator.Services, typedMessage, cancellationToken);");
                }
                else
                {
                    source.AppendLine($"{wrapperClassName}.{handlerMethodName}(scopedMediator.Services, typedMessage, cancellationToken);");
                    if (methodIsAsync)
                    {
                        source.AppendLine("return default;");
                    }
                }
            }
            else
            {
                if (handler.IsAsync)
                {
                    source.AppendLine($"return await {wrapperClassName}.{handlerMethodName}(scopedMediator.Services, typedMessage, cancellationToken);");
                }
                else if (needsValueTaskWrap)
                {
                    source.AppendLine($"return new System.Threading.Tasks.ValueTask<{responseType.UnwrappedFullName}>({wrapperClassName}.{handlerMethodName}(scopedMediator.Services, typedMessage, cancellationToken));");
                }
                else
                {
                    source.AppendLine($"return {wrapperClassName}.{handlerMethodName}(scopedMediator.Services, typedMessage, cancellationToken);");
                }
            }
        }

        source.DecrementIndent();
        source.AppendLine("}");
    }
}
