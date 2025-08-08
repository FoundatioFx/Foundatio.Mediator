using Foundatio.Mediator.Models;
using Microsoft.CodeAnalysis;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class HandlerGenerator
{
    public static void Execute(SourceProductionContext context, List<HandlerInfo> handlers, bool interceptorsEnabled)
    {
        if (handlers.Count == 0)
            return;

        Validate(context, handlers);

        foreach (var handler in handlers)
        {
            try
            {
                string wrapperClassName = GetHandlerClassName(handler);

                string source = GenerateHandler(handler, wrapperClassName, interceptorsEnabled);
                string fileName = $"{wrapperClassName}.g.cs";
                context.AddSource(fileName, source);
            }
            catch (Exception ex)
            {
                // Add diagnostic for debugging
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "FMED999",
                        "Internal source generator error",
                        $"Error generating wrapper for handler {handler.FullName}: {ex.Message}\nStackTrace: {ex.StackTrace}",
                        "Generator",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    public static string GenerateHandler(HandlerInfo handler, string wrapperClassName, bool interceptorsEnabled)
    {
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader();

        source.AppendLine($$"""
            using System;
            using System.Collections.Generic;
            using System.Diagnostics;
            using System.Diagnostics.CodeAnalysis;
            using System.Linq;
            using System.Reflection;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;

            namespace Foundatio.Mediator;
            """);

        source.AppendLine();
        source.AddGeneratedCodeAttribute();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine($"internal static class {wrapperClassName}");
        source.AppendLine("{");

        source.IncrementIndent();

        GenerateHandleMethod(source, handler);

        GenerateUntypedHandleMethod(source, handler);

        GenerateInterceptorMethods(source, handler, interceptorsEnabled);

        if (!handler.IsStatic)
        {
            GenerateGetOrCreateHandler(source, handler);
        }

        if (handler.ReturnType.IsTuple)
        {
            GeneratePublishCascadingMessages(source);
        }

        source.DecrementIndent();
        source.AppendLine("}");

        return source.ToString();
    }

    private static void GenerateHandleMethod(IndentedStringBuilder source, HandlerInfo handler)
    {
        string stronglyTypedMethodName = GetHandlerMethodName(handler);

        string asyncModifier = handler.IsAsync ? "async " : "";
        string result, accessor, parameters, defaultValue;
        bool allowNull = false;
        string returnType = handler.ReturnType.FullName;

        var variables = new Dictionary<string, string>();

        var beforeMiddleware = handler.Middleware.Where(m => m.BeforeMethod != null).Select(m => (Method: m.BeforeMethod!.Value, Middleware: m)).ToList();
        var afterMiddleware = handler.Middleware.Where(m => m.AfterMethod != null).Reverse().Select(m => (Method: m.AfterMethod!.Value, Middleware: m)).ToList();
        var finallyMiddleware = handler.Middleware.Where(m => m.FinallyMethod != null).Reverse().Select(m => (Method: m.FinallyMethod!.Value, Middleware: m)).ToList();

        var shouldUseTryCatch = finallyMiddleware.Any();

        // change return type to async if needed
        if (handler.ReturnType.IsTask == false && handler.IsAsync)
        {
            if (handler.ReturnType.IsVoid)
                returnType = "System.Threading.Tasks.ValueTask";
            else
                returnType = $"System.Threading.Tasks.ValueTask<{returnType}>";
        }

        source.AppendLine($"public static {asyncModifier}{returnType} {stronglyTypedMethodName}(Foundatio.Mediator.IMediator mediator, {handler.MessageType.FullName} message, System.Threading.CancellationToken cancellationToken)")
              .AppendLine("{");

        source.IncrementIndent();

        source.AppendLine("var serviceProvider = (System.IServiceProvider)mediator;");
        variables["System.IServiceProvider"] = "serviceProvider";
        source.AppendLine();

        // build middleware instances
        foreach (var m in handler.Middleware.Where(m => m.IsStatic == false))
        {
            source.AppendLine($"var {m.Identifier.ToCamelCase()} = Foundatio.Mediator.Mediator.GetOrCreateMiddleware<{m.FullName}>(serviceProvider);");
        }
        source.AppendLineIf(handler.Middleware.Any(m => m.IsStatic == false));

        // build result variables for before methods
        foreach (var m in beforeMiddleware.Where(m => m.Method.HasReturnValue))
        {
            allowNull = m.Method.ReturnType.IsNullable || m.Method.ReturnType.IsReferenceType;
            defaultValue = allowNull ? "null" : "default";
            source.AppendLine($"{m.Method.ReturnType.UnwrappedFullName}{(allowNull ? "?" : "")} {m.Middleware.Identifier.ToCamelCase()}Result = {defaultValue};");

            variables[m.Method.ReturnType.FullName] = $"{m.Middleware.Identifier.ToCamelCase()}Result{(allowNull ? "!" : "")}";
            if (m.Method.ReturnType.IsTuple)
            {
                foreach (var tupleItem in m.Method.ReturnType.TupleItems)
                {
                    variables[tupleItem.TypeFullName] = $"{m.Middleware.Identifier.ToCamelCase()}Result.{tupleItem.Name}{(allowNull ? "!" : "")}";
                }
            }
        }
        source.AppendLineIf(beforeMiddleware.Any(m => m.Method.HasReturnValue));

        allowNull = handler.ReturnType.IsNullable || handler.ReturnType.IsReferenceType;
        defaultValue = handler.ReturnType.IsNullable || handler.ReturnType.IsReferenceType ? "null" : "default";
        source.AppendLineIf($"{handler.ReturnType.UnwrappedFullName}{(allowNull ? "?" : "")} handlerResult = {defaultValue};", handler.HasReturnValue);

        if (shouldUseTryCatch)
        {
            source.AppendLine("""
                System.Exception? exception = null;

                try
                {
                """);

            variables["System.Exception"] = "exception";

            source.IncrementIndent();
        }

        // call before middleware
        foreach (var m in beforeMiddleware)
        {
            asyncModifier = m.Method.IsAsync ? "await " : "";
            result = m.Method.ReturnType.IsVoid ? "" : $"{m.Middleware.Identifier.ToCamelCase()}Result = ";
            accessor = m.Middleware.IsStatic ? m.Middleware.FullName : $"{m.Middleware.Identifier.ToCamelCase()}";
            parameters = BuildParameters(source, m.Method.Parameters);

            source.AppendLine($"{result}{asyncModifier}{accessor}.{m.Method.MethodName}({parameters});");

            if (m.Method.ReturnType.IsHandlerResult)
            {
                result = handler.HasReturnValue ? $" ({handler.ReturnType.UnwrappedFullName}){m.Middleware.Identifier.ToCamelCase()}Result.Value!" : "";
                if (handler.ReturnType.IsResult)
                {
                    result = $" {m.Middleware.Identifier.ToCamelCase()}Result.Value is Foundatio.Mediator.Result result ? ({handler.ReturnType.UnwrappedFullName})result : ({handler.ReturnType.UnwrappedFullName}?){m.Middleware.Identifier.ToCamelCase()}Result.Value!";
                }
                else if (handler.ReturnType.IsTuple)
                {
                    result = $" (({m.Middleware.Identifier.ToCamelCase()}Result.Value is Foundatio.Mediator.Result result ? ({handler.ReturnType.TupleItems.First().TypeFullName})result : ({handler.ReturnType.TupleItems.First().TypeFullName}?){m.Middleware.Identifier.ToCamelCase()}Result.Value!), {String.Join(", ", handler.ReturnType.TupleItems.Skip(1).Select(i => i.IsNullable ? "null" : "default"))})";
                }
                source.AppendLine($"if ({m.Middleware.Identifier.ToCamelCase()}Result.IsShortCircuited)");
                source.AppendLine("{");
                source.AppendLine($"    return{result};");
                source.AppendLine("}");
            }
        }
        source.AppendLineIf(beforeMiddleware.Any());

        // call handler
        asyncModifier = handler.ReturnType.IsTask ? "await " : "";
        result = handler.ReturnType.IsVoid ? "" : "handlerResult = ";
        accessor = handler.IsStatic ? handler.FullName : $"handlerInstance";
        parameters = BuildParameters(source, handler.Parameters);

        source.AppendLineIf("var handlerInstance = GetOrCreateHandler(serviceProvider);", !handler.IsStatic);
        source.AppendLine($"{result}{asyncModifier}{accessor}.{handler.MethodName}({parameters});");
        source.AppendLineIf(handler.HasReturnValue);

        // call after middleware
        foreach (var m in afterMiddleware)
        {
            asyncModifier = m.Method.IsAsync ? "await " : "";
            accessor = m.Middleware.IsStatic ? m.Middleware.FullName : $"{m.Middleware.Identifier.ToCamelCase()}";
            parameters = BuildParameters(source, m.Method.Parameters, variables);

            source.AppendLine($"{asyncModifier}{accessor}.{m.Method.MethodName}({parameters});");
        }
        source.AppendLineIf(afterMiddleware.Any());

        source.AppendLineIf("return handlerResult;", handler.HasReturnValue);

        if (shouldUseTryCatch)
        {
            source.DecrementIndent();

            source.AppendLine("""
                }
                catch (Exception ex)
                {
                    exception = ex;
                    throw;
                }
                finally
                {
                """);

            source.IncrementIndent();

            // call finally middleware
            foreach (var m in finallyMiddleware)
            {
                asyncModifier = m.Method.IsAsync ? "await " : "";
                accessor = m.Method.IsStatic ? m.Middleware.FullName : $"{m.Middleware.Identifier.ToCamelCase()}";
                parameters = BuildParameters(source, m.Method.Parameters, variables);

                source.AppendLine($"{asyncModifier}{accessor}.{m.Method.MethodName}({parameters});");
            }

            source.DecrementIndent();

            source.AppendLine("}");
        }

        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();
    }

    private static void GenerateUntypedHandleMethod(IndentedStringBuilder source, HandlerInfo handler)
    {
        source.AppendLine(handler.IsAsync
            ? "public static async ValueTask<object?> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)"
            : "public static object? UntypedHandle(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)");

        source.AppendLine("{");

        using (source.Indent())
        {
            source.AppendLine($"var typedMessage = ({handler.MessageType.FullName})message;");

            string stronglyTypedMethodName = GetHandlerMethodName(handler);
            string asyncModifier = handler.IsAsync ? "await " : "";
            string result = handler.ReturnType.IsVoid ? "" : "var result = ";

            source.AppendLine($"{result}{asyncModifier}{stronglyTypedMethodName}(mediator, typedMessage, cancellationToken);");

            if (handler.ReturnType.IsTuple)
            {
                source.AppendLine("return await PublishCascadingMessagesAsync(mediator, result, responseType);");
            }
            else if (handler.HasReturnValue)
            {
                source.AppendLine();
                source.AppendLine("if (responseType == null)");
                source.AppendLine("{");
                source.AppendLine("    return null;");
                source.AppendLine("}");
                source.AppendLine();

                if (handler.ReturnType.IsResult)
                {
                    source.AppendLine("""
                        if (result == null || result.GetType() == responseType || responseType.IsAssignableFrom(result.GetType()))
                        {
                            return result;
                        }

                        if (result is IResult r)
                        {
                            if (!r.IsSuccess)
                            {
                                throw new InvalidCastException($"Handler returned failed result with status {r.Status} for requested type { responseType?.Name ?? "null" }");
                            }

                            var resultValue = r.GetValue();
                            if (resultValue != null && responseType.IsAssignableFrom(resultValue.GetType()))
                            {
                                return resultValue;
                            }
                        }
                        """);
                }

                source.AppendLine(!handler.ReturnType.IsVoid ? "return result;" : "return null;");
            }
            else
            {
                source.AppendLine("return null;");
            }
        }

        source.AppendLine("}");
    }

    private static void GenerateInterceptorMethods(IndentedStringBuilder source, HandlerInfo handler, bool interceptorsEnabled)
    {
        if (!interceptorsEnabled)
            return;

        // group by mediator method and response type
        var callSiteGroups = handler.CallSites
            .GroupBy(cs => new { cs.MethodName, cs.ResponseType })
            .ToList();

        int methodCounter = 0;
        foreach (var group in callSiteGroups)
        {
            var key = group.Key;
            var groupCallSites = group.ToList();

            source.AppendLine();
            GenerateInterceptorMethod(source, handler, key.MethodName, key.ResponseType, groupCallSites, methodCounter++);
        }
    }

    private static void GenerateInterceptorMethod(IndentedStringBuilder source, HandlerInfo handler, string methodName, TypeSymbolInfo responseType, List<CallSiteInfo> callSites, int methodIndex)
    {
        string interceptorMethod = $"Intercept{methodName}{methodIndex}";
        string handlerMethod = GetHandlerMethodName(handler);
        bool methodIsAsync = methodName.EndsWith("Async") || handler.IsAsync;

        foreach (var callSite in callSites)
        {
            source.AppendLine($"[System.Runtime.CompilerServices.InterceptsLocation({callSite.Location.Version}, \"{callSite.Location.Data}\")] // {callSite.Location.DisplayLocation}");
        }

        string asyncModifier = handler.IsAsync ? "async " : "";
        string returnType = methodIsAsync ? $"System.Threading.Tasks.ValueTask<{responseType.UnwrappedFullName}>" : responseType.UnwrappedFullName;
        if (responseType.IsVoid)
            returnType = methodIsAsync ? "System.Threading.Tasks.ValueTask" : "void";
        string parameters = "this Foundatio.Mediator.IMediator mediator, object message, System.Threading.CancellationToken cancellationToken = default";
        source.AppendLine($"public static {asyncModifier}{returnType} {interceptorMethod}({parameters})");
        source.AppendLine("{");

        source.IncrementIndent();

        source.AppendLine($"var typedMessage = ({handler.MessageType.FullName})message;");

        asyncModifier = handler.IsAsync ? "await " : "";
        if (handler.ReturnType.IsTuple)
        {
            source.AppendLine($"var result = {asyncModifier}{handlerMethod}(mediator, typedMessage, cancellationToken);");
            source.AppendLine();

            var returnItem = handler.ReturnType.TupleItems.FirstOrDefault(i => i.TypeFullName == responseType.FullName);
            var publishItems = handler.ReturnType.TupleItems.Except([returnItem]);

            foreach (var publishItem in publishItems)
            {
                source.AppendLineIf($"if (result.{publishItem.Name} != null)", publishItem.IsNullable);
                source.AppendIf("    ", publishItem.IsNullable).AppendLine($"await mediator.PublishAsync(result.{publishItem.Name}, cancellationToken);");
            }
            source.AppendLineIf(publishItems.Any());

            source.AppendLine($"return result.{returnItem.Name};");
        }
        else
        {
            string returnKeyword = responseType.IsVoid ? "" : "return ";
            source.AppendLine($"{returnKeyword}{asyncModifier}{handlerMethod}(mediator, typedMessage, cancellationToken);");
        }

        if (methodIsAsync && !handler.IsAsync)
        {
            source.AppendLine("return System.Threading.Tasks.ValueTask.CompletedTask;");
        }

        source.DecrementIndent();

        source.AppendLine("}");
    }

    private static string BuildParameters(IndentedStringBuilder source, EquatableArray<ParameterInfo> parameters, Dictionary<string, string>? variables = null)
    {
        var parameterValues = new List<string>();

        const bool outputDebugInfo = false;

        foreach (var kvp in variables ?? [])
        {
            source.AppendLineIf($"// Variable: {kvp.Key} = {kvp.Value}", outputDebugInfo);
        }

        foreach (var param in parameters)
        {
            source.AppendLineIf($"// Param: Name='{param.Name}', Type.FullName='{param.Type.FullName}', Type.UnwrappedFullName='{param.Type.UnwrappedFullName}', IsMessageParameter={param.IsMessageParameter}, Type.IsObject={param.Type.IsObject}, Type.IsCancellationToken={param.Type.IsCancellationToken}", outputDebugInfo);

            if (param.IsMessageParameter)
            {
                parameterValues.Add("message");
            }
            else if (param.Type.IsObject && param.Name == "handlerResult")
            {
                parameterValues.Add("handlerResult");
            }
            else if (param.Type.IsCancellationToken)
            {
                parameterValues.Add("cancellationToken");
            }
            else if (variables != null && variables.TryGetValue(param.Type.FullName, out string? variableName))
            {
                parameterValues.Add(variableName);
            }
            else if (variables != null && variables.TryGetValue(param.Type.UnwrappedFullName, out string? unwrappedVariableName))
            {
                parameterValues.Add(unwrappedVariableName);
            }
            else if (variables != null && param.Type.UnwrappedFullName.EndsWith("?") && variables.TryGetValue(param.Type.UnwrappedFullName.TrimEnd('?'), out string? nullableVariableName))
            {
                parameterValues.Add(nullableVariableName);
            }
            else
            {
                parameterValues.Add($"serviceProvider.GetRequiredService<{param.Type.FullName}>()");
            }
        }

        return String.Join(", ", parameterValues);
    }

    private static void GeneratePublishCascadingMessages(IndentedStringBuilder source)
    {
        source.AppendLine()
              .AppendLines("""
                private static async ValueTask<object?> PublishCascadingMessagesAsync(IMediator mediator, object? result, Type? responseType)
                {
                    if (result == null)
                        return null;

                    if (result is not ITuple tuple)
                        return result;

                    object? foundResult = null;

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
                """);
    }

    private static void GenerateGetOrCreateHandler(IndentedStringBuilder source, HandlerInfo handler)
    {
        source.AppendLine()
              .AppendLines($$"""
                private static {{handler.FullName}}? _handler;
                private static readonly object _lock = new object();

                [DebuggerStepThrough]
                private static {{handler.FullName}} GetOrCreateHandler(IServiceProvider serviceProvider)
                {
                    if (_handler != null)
                        return _handler;

                    var handlerFromDI = serviceProvider.GetService<{{handler.FullName}}>();
                    if (handlerFromDI != null)
                        return handlerFromDI;

                    lock (_lock)
                    {
                        if (_handler != null)
                            return _handler;

                        _handler = ActivatorUtilities.CreateInstance<{{handler.FullName}}>(serviceProvider);
                        return _handler;
                    }
                }
                """);
    }

    public static string GetHandlerClassName(HandlerInfo handler)
    {
        return $"{handler.Identifier}_{handler.MessageType.Identifier}_Handler";
    }

    public static string GetHandlerMethodName(HandlerInfo handler)
    {
        return handler.IsAsync ? "HandleAsync" : "Handle";
    }

    private static void Validate(SourceProductionContext context, List<HandlerInfo> handlers)
    {
        var processedMiddleware = new HashSet<MiddlewareInfo>();
        foreach (var handler in handlers)
        {
            foreach (var middleware in handler.Middleware)
            {
                if (processedMiddleware.Contains(middleware))
                    continue;

                processedMiddleware.Add(middleware);

                foreach (var diagnostic in middleware.Diagnostics)
                {
                    context.ReportDiagnostic(diagnostic.ToDiagnostic());
                }
            }
        }

        ValidateCallSites(context, handlers);
    }

    private static void ValidateCallSites(SourceProductionContext context, List<HandlerInfo> handlers)
    {
        // Group handlers by message type for validation
        var handlersByMessageType = handlers
            .GroupBy(h => h.MessageType.FullName)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Collect all call sites from all handlers
        var allCallSites = handlers
            .SelectMany(h => h.CallSites)
            .GroupBy(cs => cs.MessageType.FullName)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var kvp in allCallSites)
        {
            string messageTypeName = kvp.Key;
            var callSites = kvp.Value;

            // Get handlers for this message type
            handlersByMessageType.TryGetValue(messageTypeName, out var handlersForMessage);
            handlersForMessage ??= new List<HandlerInfo>();

            foreach (var callSite in callSites)
            {
                ValidateCallSite(context, callSite, handlersForMessage);
            }
        }
    }

    // Global validation that considers all call sites discovered (including those without matching handlers)
    public static void ValidateGlobalCallSites(SourceProductionContext context, List<HandlerInfo> handlers, System.Collections.Immutable.ImmutableArray<CallSiteInfo> allDiscoveredCallSites)
    {
        // Group handlers by message type for validation
        var handlersByMessageType = handlers
            .GroupBy(h => h.MessageType.FullName)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Group all discovered call sites by message type
        var allCallSites = allDiscoveredCallSites
            .GroupBy(cs => cs.MessageType.FullName)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var kvp in allCallSites)
        {
            string messageTypeName = kvp.Key;
            var callSites = kvp.Value;

            // Get handlers for this message type
            handlersByMessageType.TryGetValue(messageTypeName, out var handlersForMessage);
            handlersForMessage ??= new List<HandlerInfo>();

            foreach (var callSite in callSites)
            {
                ValidateCallSite(context, callSite, handlersForMessage);
            }
        }
    }

    private static void ValidateCallSite(SourceProductionContext context, CallSiteInfo callSite, List<HandlerInfo> handlersForMessage)
    {
        bool isInvokeCall = callSite.MethodName is "Invoke" or "InvokeAsync";

        if (!isInvokeCall)
            return; // Only validate Invoke calls, not Publish

        // If the message is a generic type parameter (e.g., T), we cannot know the handler at compile time,
        // so do not emit FMED006/FMDE007 for missing/multiple handlers.
        if (callSite.MessageType.IsTypeParameter)
            return;

        // FMED006: No handler found for invoke call
        if (handlersForMessage.Count == 0)
        {
            var diagnostic = new DiagnosticInfo
            {
                Identifier = "FMED006",
                Title = "No handler found for message",
                Message = $"No handler found for message type '{callSite.MessageType.FullName}'. Invoke calls require exactly one handler.",
                Severity = DiagnosticSeverity.Error,
                Location = callSite.Location
            };
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
            return;
        }

        // FMED007: Multiple handlers found for invoke call
        if (handlersForMessage.Count > 1)
        {
            var handlerNames = string.Join(", ", handlersForMessage.Select(h => h.FullName));
            var diagnostic = new DiagnosticInfo
            {
                Identifier = "FMED007",
                Title = "Multiple handlers found for message",
                Message = $"Multiple handlers found for message type '{callSite.MessageType.FullName}': {handlerNames}. Invoke calls require exactly one handler. Use Publish for multiple handlers.",
                Severity = DiagnosticSeverity.Error,
                Location = callSite.Location
            };
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
            return;
        }

        var handler = handlersForMessage[0];
        bool isAsyncCall = callSite.MethodName == "InvokeAsync";

        // Evaluate specific async characteristics for precise diagnostics
        bool returnsTask = handler.ReturnType.IsTask;
        bool returnsTuple = handler.ReturnType.IsTuple;
        bool hasAsyncMiddleware = handler.Middleware.Any(m => m.IsAsync);

        // Prefer the most specific diagnostics first when using synchronous Invoke
        if (!isAsyncCall)
        {
            // FMED010: Sync invoke on handler that returns tuple
            if (returnsTuple)
            {
                var diagnostic = new DiagnosticInfo
                {
                    Identifier = "FMED010",
                    Title = "Synchronous invoke on handler with tuple return type",
                    Message = $"Cannot use synchronous 'Invoke' on handler '{handler.FullName}' that returns a tuple. Use 'InvokeAsync' instead because cascading messages need to be published asynchronously.",
                    Severity = DiagnosticSeverity.Error,
                    Location = callSite.Location
                };
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
                return;
            }

            // FMED009: Sync invoke on handler with async middleware
            if (hasAsyncMiddleware)
            {
                var asyncMiddleware = string.Join(", ", handler.Middleware.Where(m => m.IsAsync).Select(m => m.FullName));
                var diagnostic = new DiagnosticInfo
                {
                    Identifier = "FMED009",
                    Title = "Synchronous invoke on handler with asynchronous middleware",
                    Message = $"Cannot use synchronous 'Invoke' on handler '{handler.FullName}' with asynchronous middleware: {asyncMiddleware}. Use 'InvokeAsync' instead.",
                    Severity = DiagnosticSeverity.Error,
                    Location = callSite.Location
                };
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
                return;
            }

            // FMED008: Sync invoke on async handler (true async return)
            if (returnsTask)
            {
                var diagnostic = new DiagnosticInfo
                {
                    Identifier = "FMED008",
                    Title = "Synchronous invoke on asynchronous handler",
                    Message = $"Cannot use synchronous 'Invoke' on asynchronous handler '{handler.FullName}'. Use 'InvokeAsync' instead.",
                    Severity = DiagnosticSeverity.Error,
                    Location = callSite.Location
                };
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
                return;
            }
        }
    }
}
