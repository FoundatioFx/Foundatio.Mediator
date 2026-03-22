using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

/// <summary>
/// Generates interceptor methods for PublishAsync call sites.
/// </summary>
internal static class PublishInterceptorGenerator
{
    public static void Execute(
        SourceProductionContext context,
        List<CallSiteInfo> publishCallSites,
        List<HandlerInfo> allHandlers,
        GeneratorConfiguration configuration)
    {
        if (!configuration.InterceptorsEnabled)
            return;

        if (publishCallSites.Count == 0)
            return;

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

        GenerateHelperMethods(source, configuration);

        source.DecrementIndent();
        source.AppendLine("}");

        context.AddSource(hintName, source.ToString());
    }

    private static void GeneratePublishInterceptorMethod(
        IndentedStringBuilder source,
        TypeSymbolInfo messageType,
        List<CallSiteInfo> callSites,
        GeneratorConfiguration configuration,
        int messageTypeIndex)
    {
        string interceptorMethod = $"InterceptPublishAsync_{messageType.Identifier}_{messageTypeIndex}";

        foreach (var callSite in callSites)
        {
            source.AppendLine($"[InterceptsLocation({callSite.Location.Version}, \"{callSite.Location.Data}\")] // {callSite.Location.DisplayLocation}");
        }

        string returnType = "System.Threading.Tasks.ValueTask";
        string parameters = "this Foundatio.Mediator.IMediator mediator, object message, System.Threading.CancellationToken cancellationToken = default";

        source.AppendLine($"public static {returnType} {interceptorMethod}({parameters})");
        source.AppendLine("{");
        source.IncrementIndent();

        source.AppendLine($"var registry = ((global::Foundatio.Mediator.Mediator)mediator).Registry;");
        source.AppendLine($"if (registry.HasSubscribers) registry.TryWriteSubscription(message);");
        source.AppendLine($"var handlers = registry.GetPublishHandlersForType(typeof(global::{messageType.FullName}));");
        source.AppendLine();

        switch (configuration.NotificationPublishStrategy)
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
        source.AppendLines("""
            if (handlers.Length == 0) return default;

            for (int i = 0; i < handlers.Length; i++)
            {
                System.Threading.Tasks.ValueTask task;
                try
                {
                    task = handlers[i](mediator, message, cancellationToken);
                }
                catch (System.Exception ex)
                {
                    return AwaitRemainingAfterSyncThrowForeachAsync(ex, mediator, handlers, message, cancellationToken, i + 1);
                }
                if (!task.IsCompletedSuccessfully)
                {
                    return AwaitRemainingForeachAsync(task, mediator, handlers, message, cancellationToken, i + 1);
                }
            }

            return default;
            """);
    }

    private static void GenerateTaskWhenAllBody(IndentedStringBuilder source)
    {
        source.AppendLines("""
            if (handlers.Length == 0) return default;
            if (handlers.Length == 1) return handlers[0](mediator, message, cancellationToken);

            // Start all handlers concurrently
            var tasks = new System.Threading.Tasks.ValueTask[handlers.Length];
            System.Collections.Generic.List<System.Exception>? syncExceptions = null;
            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    tasks[i] = handlers[i](mediator, message, cancellationToken);
                }
                catch (System.Exception ex)
                {
                    syncExceptions ??= new System.Collections.Generic.List<System.Exception>();
                    syncExceptions.Add(ex);
                    tasks[i] = default;
                }
            }

            if (syncExceptions != null)
            {
                return AwaitAllTasksWithSyncExceptionsAsync(tasks, syncExceptions);
            }

            // Check if all completed synchronously
            for (int i = 0; i < tasks.Length; i++)
            {
                if (!tasks[i].IsCompletedSuccessfully)
                {
                    return AwaitAllTasksAsync(tasks);
                }
            }

            return default;
            """);
    }

    private static void GenerateFireAndForgetBody(IndentedStringBuilder source)
    {
        source.AppendLines("""
            for (int i = 0; i < handlers.Length; i++)
            {
                var handler = handlers[i];
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await handler(mediator, message, cancellationToken).ConfigureAwait(false);
                    }
                    catch { /* Fire and forget */ }
                }, System.Threading.CancellationToken.None);
            }

            return default;
            """);
    }

    private static void GenerateHelperMethods(IndentedStringBuilder source, GeneratorConfiguration configuration)
    {
        if (configuration.NotificationPublishStrategy == "ForeachAwait" || string.IsNullOrEmpty(configuration.NotificationPublishStrategy))
        {
            source.AppendLines("""
                private static async System.Threading.Tasks.ValueTask AwaitRemainingForeachAsync(
                    System.Threading.Tasks.ValueTask current,
                    Foundatio.Mediator.IMediator mediator,
                    global::Foundatio.Mediator.PublishAsyncDelegate[] handlers,
                    object message,
                    System.Threading.CancellationToken cancellationToken,
                    int startIndex)
                {
                    System.Collections.Generic.List<System.Exception>? exceptions = null;

                    try
                    {
                        await current.ConfigureAwait(false);
                    }
                    catch (System.Exception ex)
                    {
                        exceptions ??= new System.Collections.Generic.List<System.Exception>();
                        exceptions.Add(ex);
                    }

                    for (int i = startIndex; i < handlers.Length; i++)
                    {
                        try
                        {
                            await handlers[i](mediator, message, cancellationToken).ConfigureAwait(false);
                        }
                        catch (System.Exception ex)
                        {
                            exceptions ??= new System.Collections.Generic.List<System.Exception>();
                            exceptions.Add(ex);
                        }
                    }

                    if (exceptions != null)
                    {
                        throw new System.AggregateException(exceptions);
                    }
                }

                private static async System.Threading.Tasks.ValueTask AwaitRemainingAfterSyncThrowForeachAsync(
                    System.Exception syncException,
                    Foundatio.Mediator.IMediator mediator,
                    global::Foundatio.Mediator.PublishAsyncDelegate[] handlers,
                    object message,
                    System.Threading.CancellationToken cancellationToken,
                    int startIndex)
                {
                    var exceptions = new System.Collections.Generic.List<System.Exception> { syncException };

                    for (int i = startIndex; i < handlers.Length; i++)
                    {
                        try
                        {
                            await handlers[i](mediator, message, cancellationToken).ConfigureAwait(false);
                        }
                        catch (System.Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    }

                    throw new System.AggregateException(exceptions);
                }
                """);
        }

        if (configuration.NotificationPublishStrategy == "TaskWhenAll")
        {
            source.AppendLines("""
                private static async System.Threading.Tasks.ValueTask AwaitAllTasksAsync(System.Threading.Tasks.ValueTask[] tasks)
                {
                    System.Collections.Generic.List<System.Exception>? exceptions = null;

                    for (int i = 0; i < tasks.Length; i++)
                    {
                        try
                        {
                            await tasks[i].ConfigureAwait(false);
                        }
                        catch (System.Exception ex)
                        {
                            exceptions ??= new System.Collections.Generic.List<System.Exception>();
                            exceptions.Add(ex);
                        }
                    }

                    if (exceptions != null)
                    {
                        throw new System.AggregateException(exceptions);
                    }
                }

                private static async System.Threading.Tasks.ValueTask AwaitAllTasksWithSyncExceptionsAsync(System.Threading.Tasks.ValueTask[] tasks, System.Collections.Generic.List<System.Exception> exceptions)
                {
                    for (int i = 0; i < tasks.Length; i++)
                    {
                        try
                        {
                            await tasks[i].ConfigureAwait(false);
                        }
                        catch (System.Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    }

                    throw new System.AggregateException(exceptions);
                }
                """);
        }
    }
}
