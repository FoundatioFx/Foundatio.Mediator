using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

/// <summary>
/// Generates interceptor methods for PublishAsync call sites.
/// Uses runtime DI discovery with caching for best performance while supporting
/// handlers registered from any assembly.
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

            GeneratePublishInterceptorMethod(
                source,
                messageType,
                callSitesForMessage,
                configuration,
                messageTypeCounter++);

            source.AppendLine();
        }

        // Generate ClearCache method for testing scenarios
        GenerateClearCacheMethod(source, callSitesByMessageType);

        // Generate shared helper methods
        GenerateHelperMethods(source, configuration);

        source.DecrementIndent();
        source.AppendLine("}");

        context.AddSource(hintName, source.ToString());
    }

    private static void GenerateClearCacheMethod(IndentedStringBuilder source, List<IGrouping<string, CallSiteInfo>> callSitesByMessageType)
    {
        source.AppendLine("/// <summary>");
        source.AppendLine("/// Clears the cached handler delegates. Call this between tests that use different service providers.");
        source.AppendLine("/// </summary>");
        source.AppendLine("public static void ClearCache()");
        source.AppendLine("{");
        source.IncrementIndent();

        int index = 0;
        foreach (var group in callSitesByMessageType)
        {
            var messageType = group.First().MessageType;
            source.AppendLine($"_handlers_{messageType.Identifier}_{index} = null;");
            index++;
        }

        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();
    }

    private static void GeneratePublishInterceptorMethod(
        IndentedStringBuilder source,
        TypeSymbolInfo messageType,
        List<CallSiteInfo> callSites,
        GeneratorConfiguration configuration,
        int messageTypeIndex)
    {
        string fieldName = $"_handlers_{messageType.Identifier}_{messageTypeIndex}";
        string interceptorMethod = $"InterceptPublishAsync_{messageType.Identifier}_{messageTypeIndex}";

        // Generate static cache field for this message type
        source.AppendLine($"private static global::Foundatio.Mediator.PublishAsyncDelegate[]? {fieldName};");
        source.AppendLine();

        // Add InterceptsLocation attributes for each call site
        foreach (var callSite in callSites)
        {
            source.AppendLine($"[InterceptsLocation({callSite.Location.Version}, \"{callSite.Location.Data}\")] // {callSite.Location.DisplayLocation}");
        }

        string returnType = "System.Threading.Tasks.ValueTask";
        string parameters = "this Foundatio.Mediator.IMediator mediator, object message, System.Threading.CancellationToken cancellationToken = default";

        source.AppendLine($"public static {returnType} {interceptorMethod}({parameters})");
        source.AppendLine("{");
        source.IncrementIndent();

        // Get or initialize handlers from cache
        source.AppendLine($"var handlers = {fieldName};");
        source.AppendLine("if (handlers == null)");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine($"handlers = global::Foundatio.Mediator.Mediator.GetPublishHandlersForType(mediator, typeof(global::{messageType.FullName}));");
        source.AppendLine($"{fieldName} = handlers;");
        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();

        // Generate execution based on notification publisher strategy
        switch (configuration.NotificationPublisher)
        {
            case "ForeachAwait":
                GenerateForeachAwaitBody(source);
                break;
            case "TaskWhenAll":
                GenerateTaskWhenAllBody(source);
                break;
            case "FireAndForget":
                GenerateFireAndForgetBody(source);
                break;
            default:
                GenerateForeachAwaitBody(source);
                break;
        }

        source.DecrementIndent();
        source.AppendLine("}");
    }

    private static void GenerateForeachAwaitBody(IndentedStringBuilder source)
    {
        // Inline ForeachAwait logic - no virtual dispatch through INotificationPublisher
        source.AppendLine("if (handlers.Length == 0) return default;");
        source.AppendLine();
        source.AppendLine("// Sequential execution with sync fast-path");
        source.AppendLine("for (int i = 0; i < handlers.Length; i++)");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine("var task = handlers[i](mediator, message, cancellationToken);");
        source.AppendLine("if (!task.IsCompletedSuccessfully)");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine("return AwaitRemainingForeachAsync(task, mediator, handlers, message, cancellationToken, i + 1);");
        source.DecrementIndent();
        source.AppendLine("}");
        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("return default;");
    }

    private static void GenerateTaskWhenAllBody(IndentedStringBuilder source)
    {
        // Inline TaskWhenAll logic
        source.AppendLine("if (handlers.Length == 0) return default;");
        source.AppendLine("if (handlers.Length == 1) return handlers[0](mediator, message, cancellationToken);");
        source.AppendLine();
        source.AppendLine("// Start all handlers concurrently");
        source.AppendLine("var tasks = new System.Threading.Tasks.ValueTask[handlers.Length];");
        source.AppendLine("for (int i = 0; i < handlers.Length; i++)");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine("tasks[i] = handlers[i](mediator, message, cancellationToken);");
        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("// Check if all completed synchronously");
        source.AppendLine("for (int i = 0; i < tasks.Length; i++)");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine("if (!tasks[i].IsCompletedSuccessfully)");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine("return AwaitAllTasksAsync(tasks);");
        source.DecrementIndent();
        source.AppendLine("}");
        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("return default;");
    }

    private static void GenerateFireAndForgetBody(IndentedStringBuilder source)
    {
        // Fire and forget - queue all handlers on thread pool
        source.AppendLine("for (int i = 0; i < handlers.Length; i++)");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine("var handler = handlers[i];");
        source.AppendLine("_ = System.Threading.Tasks.Task.Run(async () =>");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine("try");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine("await handler(mediator, message, cancellationToken).ConfigureAwait(false);");
        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine("catch { /* Fire and forget */ }");
        source.DecrementIndent();
        source.AppendLine("}, System.Threading.CancellationToken.None);");
        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("return default;");
    }

    private static void GenerateHelperMethods(IndentedStringBuilder source, GeneratorConfiguration configuration)
    {
        // Generate ForeachAwait helper if needed
        if (configuration.NotificationPublisher == "ForeachAwait" || string.IsNullOrEmpty(configuration.NotificationPublisher))
        {
            source.AppendLine("private static async System.Threading.Tasks.ValueTask AwaitRemainingForeachAsync(");
            source.AppendLine("    System.Threading.Tasks.ValueTask current,");
            source.AppendLine("    Foundatio.Mediator.IMediator mediator,");
            source.AppendLine("    global::Foundatio.Mediator.PublishAsyncDelegate[] handlers,");
            source.AppendLine("    object message,");
            source.AppendLine("    System.Threading.CancellationToken cancellationToken,");
            source.AppendLine("    int startIndex)");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("System.Collections.Generic.List<System.Exception>? exceptions = null;");
            source.AppendLine();
            source.AppendLine("try");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("await current.ConfigureAwait(false);");
            source.DecrementIndent();
            source.AppendLine("}");
            source.AppendLine("catch (System.Exception ex)");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("exceptions ??= new System.Collections.Generic.List<System.Exception>();");
            source.AppendLine("exceptions.Add(ex);");
            source.DecrementIndent();
            source.AppendLine("}");
            source.AppendLine();
            source.AppendLine("for (int i = startIndex; i < handlers.Length; i++)");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("try");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("await handlers[i](mediator, message, cancellationToken).ConfigureAwait(false);");
            source.DecrementIndent();
            source.AppendLine("}");
            source.AppendLine("catch (System.Exception ex)");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("exceptions ??= new System.Collections.Generic.List<System.Exception>();");
            source.AppendLine("exceptions.Add(ex);");
            source.DecrementIndent();
            source.AppendLine("}");
            source.DecrementIndent();
            source.AppendLine("}");
            source.AppendLine();
            source.AppendLine("if (exceptions != null)");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("throw new System.AggregateException(exceptions);");
            source.DecrementIndent();
            source.AppendLine("}");
            source.DecrementIndent();
            source.AppendLine("}");
            source.AppendLine();
        }

        // Generate TaskWhenAll helper if needed
        if (configuration.NotificationPublisher == "TaskWhenAll")
        {
            source.AppendLine("private static async System.Threading.Tasks.ValueTask AwaitAllTasksAsync(System.Threading.Tasks.ValueTask[] tasks)");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("System.Collections.Generic.List<System.Exception>? exceptions = null;");
            source.AppendLine();
            source.AppendLine("for (int i = 0; i < tasks.Length; i++)");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("try");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("await tasks[i].ConfigureAwait(false);");
            source.DecrementIndent();
            source.AppendLine("}");
            source.AppendLine("catch (System.Exception ex)");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("exceptions ??= new System.Collections.Generic.List<System.Exception>();");
            source.AppendLine("exceptions.Add(ex);");
            source.DecrementIndent();
            source.AppendLine("}");
            source.DecrementIndent();
            source.AppendLine("}");
            source.AppendLine();
            source.AppendLine("if (exceptions != null)");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("throw new System.AggregateException(exceptions);");
            source.DecrementIndent();
            source.AppendLine("}");
            source.DecrementIndent();
            source.AppendLine("}");
            source.AppendLine();
        }
    }
}
