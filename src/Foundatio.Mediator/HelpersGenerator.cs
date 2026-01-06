using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class HelpersGenerator
{
    /// <summary>
    /// Generates the MediatorHelpers class with shared helper extension methods.
    /// This avoids duplicating these helpers in every generated handler class.
    /// </summary>
    public static void Execute(SourceProductionContext context, GeneratorConfiguration configuration)
    {
        const string hintName = "_MediatorHelpers.g.cs";
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, hintName);

        source.AppendLines("""
            using System;
            using System.Diagnostics.CodeAnalysis;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Foundatio.Mediator.Generated;
            """);

        source.AppendLine();
        source.AddGeneratedCodeAttribute();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLines("""
            internal static class MediatorHelpers
            {
                /// <summary>
                /// Converts a Task to ValueTask, avoiding async state machine when already completed.
                /// </summary>
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static ValueTask AsValueTask(this Task task)
                {
                    if (task.IsCompletedSuccessfully)
                        return default;

                    return AwaitTask(task);

                    static async ValueTask AwaitTask(Task task) => await task.ConfigureAwait(false);
                }

                /// <summary>
                /// Awaits a Task and returns null. Used for UntypedHandleAsync on void handlers.
                /// Avoids async state machine when task is already completed.
                /// </summary>
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static ValueTask<object?> AsNullResult(this Task task)
                {
                    if (task.IsCompletedSuccessfully)
                        return default;

                    return AwaitTask(task);

                    static async ValueTask<object?> AwaitTask(Task task)
                    {
                        await task.ConfigureAwait(false);
                        return null;
                    }
                }

                /// <summary>
                /// Awaits a ValueTask and returns null. Used for UntypedHandleAsync on void handlers.
                /// Avoids async state machine when task is already completed.
                /// </summary>
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static ValueTask<object?> AsNullResult(this ValueTask task)
                {
                    if (task.IsCompletedSuccessfully)
                        return default;

                    return AwaitTask(task);

                    static async ValueTask<object?> AwaitTask(ValueTask task)
                    {
                        await task.ConfigureAwait(false);
                        return null;
                    }
                }

                /// <summary>
                /// Converts a Task{T} to ValueTask{T}, avoiding async state machine when already completed.
                /// </summary>
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static ValueTask<T> AsValueTask<T>(this Task<T> task)
                {
                    if (task.IsCompletedSuccessfully)
                        return new ValueTask<T>(task.Result);

                    return AwaitTask(task);

                    static async ValueTask<T> AwaitTask(Task<T> task) => await task.ConfigureAwait(false);
                }

                /// <summary>
                /// Converts a ValueTask{T} to ValueTask{T}, avoiding async state machine when already completed.
                /// </summary>
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static ValueTask<T> AsValueTaskAwaited<T>(this ValueTask<T> task)
                {
                    if (task.IsCompletedSuccessfully)
                        return new ValueTask<T>(task.Result);

                    return AwaitTask(task);

                    static async ValueTask<T> AwaitTask(ValueTask<T> task) => await task.ConfigureAwait(false);
                }

                /// <summary>
                /// Publishes cascading messages from a tuple result. The first element matching the response type
                /// is returned; all other non-null elements are published via the mediator.
                /// </summary>
                public static async ValueTask<object?> PublishCascadingMessagesAsync(this IMediator mediator, object? result, Type? responseType)
                {
                    if (result == null)
                        return null;

                    if (result is not ITuple tuple)
                        return result;

                    object? foundResult = null;

                    if (responseType == typeof(object) && tuple.Length > 0)
                    {
                        foundResult = tuple[0];
                        for (int i = 1; i < tuple.Length; i++)
                        {
                            var item = tuple[i];
                            if (item != null)
                                await mediator.PublishAsync(item, CancellationToken.None);
                        }

                        return foundResult;
                    }

                    for (int i = 0; i < tuple.Length; i++)
                    {
                        var item = tuple[i];
                        if (item != null && responseType != null && responseType.IsAssignableFrom(item.GetType()))
                        {
                            foundResult = item;
                        }
                        else if (item != null)
                        {
                            await mediator.PublishAsync(item, CancellationToken.None);
                        }
                    }

                    return foundResult;
                }
            }
            """);

        context.AddSource(hintName, source.ToString());
    }
}
