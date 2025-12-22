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
        bool interceptorsEnabled)
    {
        if (!interceptorsEnabled)
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

        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader();

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

        bool hasAsyncHandlers = false;
        bool hasSyncHandlers = false;

        int messageTypeCounter = 0;
        foreach (var group in crossAssemblyCallSites)
        {
            var messageTypeName = group.Key;
            var handler = handlersByMessageType[messageTypeName];
            var callSitesForMessage = group.ToList();

            if (handler.IsAsync)
                hasAsyncHandlers = true;
            else
                hasSyncHandlers = true;

            // Group call sites by method name and response type
            var callSiteGroups = callSitesForMessage
                .GroupBy(cs => new { cs.MethodName, cs.ResponseType })
                .ToList();

            int methodCounter = 0;
            foreach (var csGroup in callSiteGroups)
            {
                GenerateInterceptorMethod(source, handler, csGroup.Key.MethodName, csGroup.Key.ResponseType, csGroup.ToList(), messageTypeCounter, methodCounter++);
                source.AppendLine();
            }

            messageTypeCounter++;
        }

        // Generate shared helper methods
        GenerateGetOrCreateScope(source, hasAsyncHandlers, hasSyncHandlers);

        source.DecrementIndent();
        source.AppendLine("}");

        context.AddSource("_CrossAssemblyInterceptors.g.cs", source.ToString());
    }

    private static void GenerateInterceptorMethod(
        IndentedStringBuilder source,
        HandlerInfo handler,
        string methodName,
        TypeSymbolInfo responseType,
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

        string asyncModifier = handler.IsAsync ? "async " : "";
        string returnType = methodIsAsync ? $"System.Threading.Tasks.ValueTask<{responseType.UnwrappedFullName}>" : responseType.UnwrappedFullName;
        if (responseType.IsVoid)
            returnType = methodIsAsync ? "System.Threading.Tasks.ValueTask" : "void";
        string parameters = "this Foundatio.Mediator.IMediator mediator, object message, System.Threading.CancellationToken cancellationToken = default";

        source.AppendLine($"public static {asyncModifier}{returnType} {interceptorMethod}({parameters})");
        source.AppendLine("{");
        source.IncrementIndent();

        if (handler.IsAsync)
        {
            source.AppendLine("await using var handlerScope = await GetOrCreateScopeAsync(mediator, cancellationToken);");
        }
        else
        {
            source.AppendLine("using var handlerScope = GetOrCreateScope(mediator, cancellationToken);");
        }
        source.AppendLine($"var typedMessage = (global::{handler.MessageType.FullName})message;");

        // Call the handler in the referenced assembly
        string awaitKeyword = handler.IsAsync ? "await " : "";

        if (handler.ReturnType.IsTuple)
        {
            // Handle tuple return types (cascading messages)
            source.AppendLine($"var result = {awaitKeyword}{wrapperClassName}.{handlerMethodName}(handlerScope.Services, typedMessage, cancellationToken);");
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
                source.AppendIf("    ", publishItem.IsNullable).AppendLine($"await mediator.PublishAsync(result.{publishItem.Name}, cancellationToken);");
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
                source.AppendLine($"{awaitKeyword}{wrapperClassName}.{handlerMethodName}(handlerScope.Services, typedMessage, cancellationToken);");

                if (needsValueTaskWrap)
                {
                    source.AppendLine("return System.Threading.Tasks.ValueTask.CompletedTask;");
                }
            }
            else
            {
                if (needsValueTaskWrap)
                {
                    source.AppendLine($"return new System.Threading.Tasks.ValueTask<{responseType.UnwrappedFullName}>({wrapperClassName}.{handlerMethodName}(handlerScope.Services, typedMessage, cancellationToken));");
                }
                else
                {
                    source.AppendLine($"return {awaitKeyword}{wrapperClassName}.{handlerMethodName}(handlerScope.Services, typedMessage, cancellationToken);");
                }
            }
        }

        source.DecrementIndent();
        source.AppendLine("}");
    }

    private static void GenerateGetOrCreateScope(IndentedStringBuilder source, bool hasAsyncHandlers, bool hasSyncHandlers)
    {
        if (hasSyncHandlers)
        {
            source.AppendLines("""
                [DebuggerStepThrough]
                private static HandlerScopeValue GetOrCreateScope(IMediator mediator, CancellationToken cancellationToken)
                {
                    return HandlerScope.GetOrCreate(mediator, cancellationToken);
                }
                """);
        }
        if (hasAsyncHandlers)
        {
            if (hasSyncHandlers)
                source.AppendLine();
            source.AppendLines("""
                [DebuggerStepThrough]
                private static System.Threading.Tasks.ValueTask<HandlerScopeValue> GetOrCreateScopeAsync(IMediator mediator, CancellationToken cancellationToken)
                {
                    return HandlerScope.GetOrCreateAsync(mediator, cancellationToken);
                }
                """);
        }
    }
}
