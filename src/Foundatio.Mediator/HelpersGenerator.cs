using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class HelpersGenerator
{
    /// <summary>
    /// Generates the MediatorHelpers class with shared helper extension methods.
    /// This avoids duplicating these helpers in every generated handler class.
    /// </summary>
    public static void Execute(SourceProductionContext context)
    {
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader();

        source.AppendLines("""
            using System;
            using System.Diagnostics.CodeAnalysis;
            using System.Runtime.CompilerServices;
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
                /// Awaits a Task and returns nothing. Used for void-returning async handlers.
                /// </summary>
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static async ValueTask AsValueTask(this Task task)
                {
                    await task.ConfigureAwait(false);
                }

                /// <summary>
                /// Awaits a Task and returns null. Used for UntypedHandleAsync on void handlers.
                /// </summary>
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static async ValueTask<object?> AsNullResult(this Task task)
                {
                    await task.ConfigureAwait(false);
                    return null;
                }

                /// <summary>
                /// Awaits a Task{T} and returns the result wrapped in ValueTask{T}.
                /// </summary>
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static async ValueTask<T> AsValueTask<T>(this Task<T> task)
                {
                    return await task.ConfigureAwait(false);
                }

                /// <summary>
                /// Awaits a ValueTask{T} and returns the result. Used for handlers returning ValueTask{T}.
                /// </summary>
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static async ValueTask<T> AsValueTask<T>(this ValueTask<T> task)
                {
                    return await task.ConfigureAwait(false);
                }
            }
            """);

        context.AddSource("_MediatorHelpers.g.cs", source.ToString());
    }
}
