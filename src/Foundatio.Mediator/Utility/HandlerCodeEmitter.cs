using Foundatio.Mediator.Models;

namespace Foundatio.Mediator.Utility;

/// <summary>
/// Shared utility for generating handler invocation code.
/// Used by HandlerGenerator, PublishInterceptorGenerator, and cascading handler generation.
/// </summary>
internal static class HandlerCodeEmitter
{
    /// <summary>
    /// Determines if a handler method is async (either returns Task/ValueTask or has tuple return for cascading).
    /// </summary>
    public static bool IsHandlerAsync(HandlerInfo handler)
        => handler.IsAsync || handler.ReturnType.IsTuple;

    /// <summary>
    /// Gets the wrapper class name for a handler (fully qualified with global:: prefix).
    /// </summary>
    public static string GetWrapperClassName(HandlerInfo handler)
        => $"global::{HandlerGenerator.GetHandlerFullName(handler)}";

    /// <summary>
    /// Emits a handler call with try-catch for exception aggregation.
    /// Used by both PublishInterceptorGenerator and cascading handler generation.
    /// </summary>
    /// <param name="source">The string builder to emit code to.</param>
    /// <param name="handler">The handler info.</param>
    /// <param name="messageVar">Variable name containing the message.</param>
    /// <param name="cancellationTokenVar">Variable name for cancellation token.</param>
    /// <param name="mediatorVar">Variable name for the mediator (default: "mediator").</param>
    public static void EmitHandlerCallWithTryCatch(
        IndentedStringBuilder source,
        HandlerInfo handler,
        string messageVar,
        string cancellationTokenVar,
        string mediatorVar = "mediator")
    {
        string wrapperClassName = GetWrapperClassName(handler);
        string methodName = HandlerGenerator.GetHandlerMethodName(handler);
        bool isAsync = IsHandlerAsync(handler);

        source.AppendLine("try");
        source.AppendLine("{");
        if (isAsync)
        {
            source.AppendLine($"    await {wrapperClassName}.{methodName}({mediatorVar}, {messageVar}, {cancellationTokenVar}).ConfigureAwait(false);");
        }
        else
        {
            source.AppendLine($"    {wrapperClassName}.{methodName}({mediatorVar}, {messageVar}, {cancellationTokenVar});");
        }
        source.AppendLine("}");
        EmitExceptionAggregation(source);
    }

    /// <summary>
    /// Emits a single-line try-catch for sync handler calls.
    /// Used for compact exception handling in TaskWhenAll strategy.
    /// </summary>
    public static void EmitInlineTryCatch(
        IndentedStringBuilder source,
        HandlerInfo handler,
        string messageVar,
        string cancellationTokenVar,
        string mediatorVar = "mediator")
    {
        string wrapperClassName = GetWrapperClassName(handler);
        string methodName = HandlerGenerator.GetHandlerMethodName(handler);
        source.AppendLine($"try {{ {wrapperClassName}.{methodName}({mediatorVar}, {messageVar}, {cancellationTokenVar}); }} catch (System.Exception ex) {{ exceptions ??= new System.Collections.Generic.List<System.Exception>(); exceptions.Add(ex); }}");
    }

    /// <summary>
    /// Emits a fire-and-forget handler call wrapped in Task.Run.
    /// Used for background execution that doesn't block the caller.
    /// </summary>
    public static void EmitFireAndForgetHandlerCall(
        IndentedStringBuilder source,
        HandlerInfo handler,
        string messageVar,
        string mediatorVar = "mediator")
    {
        string wrapperClassName = GetWrapperClassName(handler);
        string methodName = HandlerGenerator.GetHandlerMethodName(handler);
        bool isAsync = IsHandlerAsync(handler);

        source.AppendLine("_ = System.Threading.Tasks.Task.Run(async () =>");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine("try");
        source.AppendLine("{");
        if (isAsync)
        {
            source.AppendLine($"    await {wrapperClassName}.{methodName}({mediatorVar}, {messageVar}, System.Threading.CancellationToken.None).ConfigureAwait(false);");
        }
        else
        {
            source.AppendLine($"    {wrapperClassName}.{methodName}({mediatorVar}, {messageVar}, System.Threading.CancellationToken.None);");
        }
        source.AppendLine("}");
        source.AppendLine("catch");
        source.AppendLine("{");
        source.AppendLine("    // Swallow exceptions - fire and forget semantics");
        source.AppendLine("}");
        source.DecrementIndent();
        source.AppendLine("}, System.Threading.CancellationToken.None);");
    }

    /// <summary>
    /// Emits the standard exception aggregation catch block.
    /// </summary>
    public static void EmitExceptionAggregation(IndentedStringBuilder source)
    {
        source.AppendLine("catch (System.Exception ex)");
        source.AppendLine("{");
        source.AppendLine("    exceptions ??= new System.Collections.Generic.List<System.Exception>();");
        source.AppendLine("    exceptions.Add(ex);");
        source.AppendLine("}");
    }

    /// <summary>
    /// Emits the exception list declaration for handlers that aggregate exceptions.
    /// </summary>
    public static void EmitExceptionListDeclaration(IndentedStringBuilder source)
    {
        source.AppendLine("System.Collections.Generic.List<System.Exception>? exceptions = null;");
    }

    /// <summary>
    /// Emits the aggregate exception throw if any exceptions were collected.
    /// </summary>
    public static void EmitAggregateExceptionThrow(IndentedStringBuilder source)
    {
        source.AppendLine("if (exceptions != null)");
        source.AppendLine("    throw new System.AggregateException(exceptions);");
    }

    /// <summary>
    /// Starts an async task variable for a handler call (used in TaskWhenAll strategy).
    /// </summary>
    public static string EmitAsyncTaskStart(
        IndentedStringBuilder source,
        HandlerInfo handler,
        string messageVar,
        string cancellationTokenVar,
        int taskIndex,
        string mediatorVar = "mediator")
    {
        string wrapperClassName = GetWrapperClassName(handler);
        string methodName = HandlerGenerator.GetHandlerMethodName(handler);
        string varName = $"t{taskIndex}";
        source.AppendLine($"var {varName} = {wrapperClassName}.{methodName}({mediatorVar}, {messageVar}, {cancellationTokenVar});");
        return varName;
    }

    /// <summary>
    /// Emits await statements for a list of task variables with exception handling.
    /// </summary>
    public static void EmitAwaitTasksWithExceptionHandling(
        IndentedStringBuilder source,
        List<string> taskVars)
    {
        foreach (var varName in taskVars)
        {
            source.AppendLine($"try {{ await {varName}.ConfigureAwait(false); }} catch (System.Exception ex) {{ exceptions ??= new System.Collections.Generic.List<System.Exception>(); exceptions.Add(ex); }}");
        }
    }
}
