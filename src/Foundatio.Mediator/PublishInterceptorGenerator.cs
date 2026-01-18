using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

/// <summary>
/// Generates interceptor methods for PublishAsync call sites.
/// This enables compile-time dispatch to all handlers in the correct order,
/// eliminating runtime handler discovery via DI.
/// </summary>
internal static class PublishInterceptorGenerator
{
    public static void Execute(
        SourceProductionContext context,
        List<CallSiteInfo> publishCallSites,
        List<HandlerInfo> allHandlers,
        GeneratorConfiguration configuration)
    {
        // Publish interceptors are controlled by InterceptorsEnabled
        if (!configuration.InterceptorsEnabled)
            return;

        if (publishCallSites.Count == 0)
            return;

        // Group call sites by message type
        var callSitesByMessageType = publishCallSites
            .GroupBy(cs => cs.MessageType.FullName)
            .ToList();

        if (callSitesByMessageType.Count == 0)
            return;

        const string hintName = "_PublishInterceptors.g.cs";
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, hintName);

        source.AppendLines("""
            using System;
            using System.Collections.Generic;
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
        source.AppendLine("internal static class PublishInterceptors");
        source.AppendLine("{");
        source.IncrementIndent();

        int messageTypeCounter = 0;
        foreach (var group in callSitesByMessageType)
        {
            var firstCallSite = group.First();
            var messageType = firstCallSite.MessageType;
            var callSitesForMessage = group.ToList();

            // Find all handlers applicable to this message type (including interface/base class handlers)
            var applicableHandlers = GetApplicableHandlers(firstCallSite, allHandlers);

            // Only generate interceptor if we have handlers
            if (applicableHandlers.Count == 0)
                continue;

            GeneratePublishInterceptorMethod(
                source,
                messageType,
                callSitesForMessage,
                applicableHandlers,
                configuration,
                messageTypeCounter++);

            source.AppendLine();
        }

        source.DecrementIndent();
        source.AppendLine("}");

        context.AddSource(hintName, source.ToString());
    }

    /// <summary>
    /// Gets all handlers applicable to a message type, including handlers for interfaces and base classes.
    /// Returns handlers sorted by Order (ascending), then specificity (concrete before interface/base), then by FullName.
    /// </summary>
    private static List<HandlerInfo> GetApplicableHandlers(CallSiteInfo callSite, List<HandlerInfo> allHandlers)
    {
        var messageType = callSite.MessageType;
        var applicable = new List<(HandlerInfo Handler, int Specificity)>();

        foreach (var handler in allHandlers)
        {
            // Exact type match (most specific)
            if (handler.MessageType.FullName == messageType.FullName)
            {
                applicable.Add((handler, 0));
                continue;
            }

            // Check if handler handles an interface implemented by the message type
            if (handler.MessageType.IsInterface && callSite.MessageInterfaces.Contains(handler.MessageType.FullName))
            {
                applicable.Add((handler, 1));
                continue;
            }

            // Check if handler handles a base class of the message type
            if (callSite.MessageBaseClasses.Contains(handler.MessageType.FullName))
            {
                applicable.Add((handler, 2));
                continue;
            }
        }

        // Sort by Order (ascending), then by specificity (concrete first), then by FullName for deterministic ordering
        return applicable
            .OrderBy(x => x.Handler.Order)
            .ThenBy(x => x.Specificity)
            .ThenBy(x => x.Handler.FullName)
            .Select(x => x.Handler)
            .ToList();
    }

    private static void GeneratePublishInterceptorMethod(
        IndentedStringBuilder source,
        TypeSymbolInfo messageType,
        List<CallSiteInfo> callSites,
        List<HandlerInfo> handlers,
        GeneratorConfiguration configuration,
        int messageTypeIndex)
    {
        string interceptorMethod = $"InterceptPublishAsync_{messageType.Identifier}_{messageTypeIndex}";

        // Add InterceptsLocation attributes for each call site
        foreach (var callSite in callSites)
        {
            source.AppendLine($"[InterceptsLocation({callSite.Location.Version}, \"{callSite.Location.Data}\")] // {callSite.Location.DisplayLocation}");
        }

        // Check if any handler has cascading messages (tuple return)
        bool hasCascadingHandlers = handlers.Any(h => h.ReturnType.IsTuple);
        // Check if any handler is async
        bool hasAsyncHandlers = handlers.Any(h => h.IsAsync);

        // Determine async modifier based on strategy and handler characteristics
        // ForeachAwait: needs async if any handler is async (sync handlers don't need await)
        // TaskWhenAll: always async for concurrent execution
        // FireAndForget: never async - fires and returns immediately
        bool needsAsync = configuration.NotificationPublisher switch
        {
            "ForeachAwait" => hasAsyncHandlers || hasCascadingHandlers,
            "TaskWhenAll" => true,
            "FireAndForget" => false,
            _ => hasAsyncHandlers || hasCascadingHandlers
        };

        string asyncModifier = needsAsync ? "async " : "";
        string returnType = "System.Threading.Tasks.ValueTask";
        string parameters = "this Foundatio.Mediator.IMediator mediator, object message, System.Threading.CancellationToken cancellationToken = default";

        source.AppendLine($"public static {asyncModifier}{returnType} {interceptorMethod}({parameters})");
        source.AppendLine("{");
        source.IncrementIndent();

        switch (configuration.NotificationPublisher)
        {
            case "ForeachAwait":
                GenerateForeachAwaitBody(source, messageType, handlers, hasAsyncHandlers, hasCascadingHandlers);
                break;
            case "TaskWhenAll":
                GenerateTaskWhenAllBody(source, messageType, handlers, hasCascadingHandlers);
                break;
            case "FireAndForget":
                GenerateFireAndForgetBody(source, messageType, handlers);
                break;
            default:
                GenerateForeachAwaitBody(source, messageType, handlers, hasAsyncHandlers, hasCascadingHandlers);
                break;
        }

        source.DecrementIndent();
        source.AppendLine("}");
    }

    private static void GenerateForeachAwaitBody(
        IndentedStringBuilder source,
        TypeSymbolInfo messageType,
        List<HandlerInfo> handlers,
        bool hasAsyncHandlers,
        bool hasCascadingHandlers)
    {
        // Each handler creates its own scope internally, so no scope creation here
        source.AppendLine($"var typedMessage = (global::{messageType.FullName})message;");
        source.AppendLine();

        // Exception aggregation
        HandlerCodeEmitter.EmitExceptionListDeclaration(source);
        source.AppendLine();

        foreach (var handler in handlers)
        {
            HandlerCodeEmitter.EmitHandlerCallWithTryCatch(source, handler, "typedMessage", "cancellationToken");
            source.AppendLine();
        }

        // Throw aggregate exception if any handlers failed
        HandlerCodeEmitter.EmitAggregateExceptionThrow(source);

        // If no async handlers, we need an explicit return for the ValueTask return type
        if (!hasAsyncHandlers && !hasCascadingHandlers)
        {
            source.AppendLine();
            source.AppendLine("return default;");
        }
    }

    private static void GenerateTaskWhenAllBody(
        IndentedStringBuilder source,
        TypeSymbolInfo messageType,
        List<HandlerInfo> handlers,
        bool hasCascadingHandlers)
    {
        // Each handler creates its own scope internally
        source.AppendLine($"var typedMessage = (global::{messageType.FullName})message;");
        source.AppendLine();

        // Separate handlers by async/sync and start async tasks
        var asyncHandlerVars = new List<string>();
        var syncHandlers = new List<HandlerInfo>();
        int asyncTaskIndex = 0;

        foreach (var handler in handlers)
        {
            if (HandlerCodeEmitter.IsHandlerAsync(handler))
            {
                string varName = HandlerCodeEmitter.EmitAsyncTaskStart(
                    source, handler, "typedMessage", "cancellationToken", asyncTaskIndex++);
                asyncHandlerVars.Add(varName);
            }
            else
            {
                syncHandlers.Add(handler);
            }
        }

        source.AppendLine();
        HandlerCodeEmitter.EmitExceptionListDeclaration(source);

        // Execute sync handlers first (they're quick, no need to parallelize)
        foreach (var handler in syncHandlers)
        {
            HandlerCodeEmitter.EmitInlineTryCatch(source, handler, "typedMessage", "cancellationToken");
        }

        if (asyncHandlerVars.Count > 0)
        {
            source.AppendLine();

            // Check for synchronous completion of async handlers
            var syncCheckConditions = string.Join(" && ", asyncHandlerVars.Select(v => $"{v}.IsCompletedSuccessfully"));
            source.AppendLine($"if (!({syncCheckConditions}))");
            source.AppendLine("{");
            source.IncrementIndent();

            // Await all async handlers with exception handling
            HandlerCodeEmitter.EmitAwaitTasksWithExceptionHandling(source, asyncHandlerVars);

            source.DecrementIndent();
            source.AppendLine("}");
        }

        source.AppendLine();
        HandlerCodeEmitter.EmitAggregateExceptionThrow(source);
    }

    private static void GenerateFireAndForgetBody(
        IndentedStringBuilder source,
        TypeSymbolInfo messageType,
        List<HandlerInfo> handlers)
    {
        source.AppendLine($"var typedMessage = (global::{messageType.FullName})message;");
        source.AppendLine();

        foreach (var handler in handlers)
        {
            HandlerCodeEmitter.EmitFireAndForgetHandlerCall(source, handler, "typedMessage");
            source.AppendLine();
        }

        source.AppendLine("return default;");
    }
}
