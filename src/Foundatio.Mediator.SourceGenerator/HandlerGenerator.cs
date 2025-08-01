using Microsoft.CodeAnalysis;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class HandlerGenerator
{
    public static void Execute(SourceProductionContext context, List<HandlerInfo> handlers, bool interceptorsEnabled)
    {
        if (handlers == null || handlers.Count == 0)
            return;

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
            using System;

            namespace Foundatio.Mediator;

            [ExcludeFromCodeCoverage]
            internal static class {{wrapperClassName}}
            {
            """);

        source.IncrementIndent();

        GenerateHandleMethod(source, handler);

        // generate untyped handle method

        GenerateInterceptorMethods(source, handler);

        if (!handler.IsStatic)
        {
            GenerateGetOrCreateHandler(source, handler);
        }

        if (!handler.ReturnType.IsVoid && handler.ReturnType.IsTuple)
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
        string result, accessor, parameters;
        string returnType = handler.ReturnType.FullName;

        var variables = new Dictionary<string, string>();

        var beforeMiddleware = handler.Middleware.Where(m => m.BeforeMethod != null).Select(m => (Method: m.BeforeMethod!.Value, Middleware: m)).ToList();
        var afterMiddleware = handler.Middleware.Where(m => m.AfterMethod != null).Select(m => (Method: m.AfterMethod!.Value, Middleware: m)).ToList();
        var finallyMiddleware = handler.Middleware.Where(m => m.FinallyMethod != null).Select(m => (Method: m.FinallyMethod!.Value, Middleware: m)).ToList();

        var shouldUseTryCatch = finallyMiddleware.Any();

        // change return type to async if needed
        if (handler.ReturnType.IsTask == false && handler.IsAsync)
        {
            if (handler.ReturnType.IsVoid)
                returnType = "global::System.Threading.Tasks.ValueTask";
            else
                returnType = $"global::System.Threading.Tasks.ValueTask<global::{returnType}>";
        }

        source.AppendLine($"public static {asyncModifier}{returnType} {stronglyTypedMethodName}(global::Foundatio.Mediator.IMediator mediator, global::{handler.MessageType.FullName} message, global::System.Threading.CancellationToken cancellationToken)")
              .AppendLine("{");

        source.IncrementIndent();

        source.AppendLine("var serviceProvider = (global::System.IServiceProvider)mediator;");
        variables["System.IServiceProvider"] = "serviceProvider";
        source.AppendLine();

        // build middleware instances
        foreach (var m in handler.Middleware.Where(m => m.IsStatic == false))
        {
            source.AppendLine($"var middleware{m.Identifier} = global::Foundatio.Mediator.Mediator.GetOrCreateMiddleware<{m.FullName}>(serviceProvider);");
        }

        source.AppendLine();

        // build result variables for before methods
        foreach (var m in beforeMiddleware.Where(m => m.Method.HasReturnValue))
        {
            var defaultValue = m.Method.ReturnType.IsNullable ? "null" : "default";
            source.AppendLine($"global::{m.Method.ReturnType.FullName} result{m.Middleware.Identifier} = {defaultValue};");
        }

        source.AppendLine();

        if (shouldUseTryCatch)
        {
            source.AppendLine("""
                global::System.Exception? exception = null;");

                try
                {
                """);

            source.IncrementIndent();
        }

        // call before middleware
        foreach (var m in beforeMiddleware)
        {
            asyncModifier = m.Middleware.IsAsync ? "await " : "";
            result = m.Method.ReturnType.IsVoid ? "" : $"result{m.Middleware.Identifier} = ";
            accessor = m.Middleware.IsStatic ? m.Middleware.FullName : $"middleware{m.Middleware.Identifier}";
            parameters = BuildParameters(m.Method.Parameters);

            source.AppendLine($"{result}{asyncModifier}{accessor}.{m.Method.MethodName}({parameters});");
        }
        source.AppendLineIf(beforeMiddleware.Any());

        // call handler
        asyncModifier = handler.IsAsync ? "await " : "";
        result = handler.ReturnType.IsVoid ? "" : $"var handlerResult = ";
        accessor = handler.IsStatic ? handler.FullName : $"handlerInstance";
        parameters = BuildParameters(handler.Parameters);
        source.AppendLineIf("var handlerInstance = GetOrCreateHandler(serviceProvider);", !handler.IsStatic);
        source.AppendLine($"{result}{asyncModifier}{accessor}.{handler.MethodName}({parameters});");
        source.AppendLine();

        // call after middleware
        foreach (var m in afterMiddleware)
        {
            asyncModifier = m.Middleware.IsAsync ? "await " : "";
            accessor = m.Middleware.IsStatic ? m.Middleware.FullName : $"middleware{m.Middleware.Identifier}";
            parameters = BuildParameters(m.Method.Parameters);

            source.AppendLine($"{asyncModifier}{accessor}.{m.Method.MethodName}({parameters});");
        }
        source.AppendLineIf(afterMiddleware.Any());

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
                accessor = m.Method.IsStatic ? m.Middleware.FullName : $"middleware{m.Middleware.Identifier}";
                parameters = BuildParameters(m.Method.Parameters);

                source.AppendLine($"{asyncModifier}{accessor}.{m.Method.MethodName}({parameters});");
            }

            source.DecrementIndent();

            source.AppendLine("}");
            source.AppendLine();

            source.DecrementIndent();
        }

        if (handler.HasReturnValue)
        {
            source.AppendLine("return handlerResult;");
        }

        source.AppendLine("}");
        source.AppendLine();
    }

    private static string BuildParameters(EquatableArray<ParameterInfo> parameters)
    {
        var parameterValues = new List<string>();

        foreach (var param in parameters)
        {
            if (param.IsMessageParameter)
            {
                parameterValues.Add("message");
            }
            else if (param.Type.IsCancellationToken)
            {
                parameterValues.Add("cancellationToken");
            }
            else
            {
                // This is a dependency that needs to be resolved from DI
                parameterValues.Add($"serviceProvider.GetRequiredService<{param.Type.FullName}>()");
            }
        }

        return String.Join(", ", parameterValues);
    }

    private static void GenerateUntypedHandleMethod(IndentedStringBuilder source, HandlerInfo handler)
    {
        if (handler.IsAsync)
        {
            source.AppendLine("public static async ValueTask<object?> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)");
        }
        else
        {
            source.AppendLine("public static object? UntypedHandle(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)");
        }

        source.AppendLine("{");

        using (source.Indent())
        {
            source.AppendLine($"var typedMessage = ({handler.MessageType.FullName})message;");

            string stronglyTypedMethodName = GetHandlerMethodName(handler);

            if (!handler.ReturnType.IsVoid)
            {
                source.AppendLine($"var result = {(handler.IsAsync ? "await " : "")}{stronglyTypedMethodName}(mediator, typedMessage, cancellationToken);");

                if (handler.ReturnType.IsTuple)
                {
                    source.AppendLine("return await PublishCascadingMessagesAsync(mediator, result, responseType);");
                }
                else
                {
                    GenerateNonTupleResultHandling(source, handler);
                }
            }
        }
    }

    private static void GenerateNonTupleResultHandling(IndentedStringBuilder source, HandlerInfo handler)
    {
        source.AppendLine();
        source.AppendLine("if (responseType == null)");
        source.AppendLine("{");
        using (source.Indent())
        {
            source.AppendLine("return null;");
        }
        source.AppendLine("}");
        source.AppendLine();

        if (handler.ReturnType.IsResult)
        {
            source.AppendLine("if (result == null || result.GetType() == responseType || responseType.IsAssignableFrom(result.GetType()))");
            source.AppendLine("{");
            using (source.Indent())
            {
                source.AppendLine("return result;");
            }
            source.AppendLine("}");
            source.AppendLine();

            source.AppendLine("if (result is IResult r)");
            source.AppendLine("{");
            using (source.Indent())
            {
                source.AppendLine("if (!r.IsSuccess)");
                source.AppendLine("{");
                using (source.Indent())
                {
                    source.AppendLine("throw new InvalidCastException($\"Handler returned failed result with status {r.Status} for requested type {responseType?.Name ?? \"null\"}\");");
                }
                source.AppendLine("}");
                source.AppendLine();
                source.AppendLine("var resultValue = r.GetValue();");
                source.AppendLine("if (resultValue != null && responseType.IsAssignableFrom(resultValue.GetType()))");
                source.AppendLine("{");
                using (source.Indent())
                {
                    source.AppendLine("return resultValue;");
                }
                source.AppendLine("}");
            }
            source.AppendLine("}");
            source.AppendLine();
        }

        if (!handler.ReturnType.IsVoid)
            source.AppendLine("return result;");
        else
            source.AppendLine("return null;");
    }

    private static void GenerateInterceptorMethods(IndentedStringBuilder source, HandlerInfo handler)
    {
        // Group call sites by method signature to generate unique interceptor methods
        var callSiteGroups = handler.CallSites
            .GroupBy(cs => new { cs.MethodName, MessageTypeName = cs.MessageType.FullName, ResponseTypeName = cs.ResponseType?.FullName })
            .ToList();

        int methodCounter = 0;
        foreach (var group in callSiteGroups)
        {
            var key = group.Key;
            var groupCallSites = group.ToList();

            GenerateInterceptorMethod(source, handler, key.MethodName, key.ResponseTypeName ?? "", groupCallSites, methodCounter++, handler.Middleware.ToList());
        }
    }

    private static void GenerateInterceptorMethod(IndentedStringBuilder source, HandlerInfo handler, string methodName, string expectedResponseTypeName, List<CallSiteInfo> callSites, int methodIndex, List<MiddlewareInfo> middlewares)
    {
        // Generate unique method name for the interceptor
        string interceptorMethodName = $"Intercept{methodName}{methodIndex}";

        // Determine if the wrapper method is async (either because the handler is async OR because there are async middleware)
        bool hasAsyncMiddleware = middlewares.Any(m =>
            (m.BeforeMethod?.IsAsync == true) ||
            (m.AfterMethod?.IsAsync == true) ||
            (m.FinallyMethod?.IsAsync == true));

        bool wrapperIsAsync = handler.IsAsync || hasAsyncMiddleware;

        // The interceptor should match the original call signature (Invoke vs InvokeAsync)
        bool interceptorIsAsync = methodName.EndsWith("Async");
        bool isGeneric = !String.IsNullOrEmpty(expectedResponseTypeName);

        // Check for sync interceptor with async wrapper - this should generate a diagnostic error instead
        if (!interceptorIsAsync && wrapperIsAsync)
        {
            // Don't generate interceptor for sync calls with async middleware
            // This will be caught by the validator and generate FMED012 diagnostic
            return;
        }

        // Generate interceptor attributes for all call sites
        var interceptorAttributes = callSites
            .Select(GenerateInterceptorAttribute)
            .Where(attr => !String.IsNullOrEmpty(attr))
            .ToList();

        if (interceptorAttributes.Count == 0)
            return;

        source.AppendLine();

        // Add interceptor attributes
        foreach (string? attribute in interceptorAttributes)
        {
            source.AppendLine($"{attribute}");
        }

        // Generate method signature
        string returnType = GenerateInterceptorReturnType(interceptorIsAsync, isGeneric, expectedResponseTypeName);
        string parameters = "this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default";
        string stronglyTypedMethodName = GetHandlerMethodName(handler);

        string asyncModifier = interceptorIsAsync ? "async " : "";
        source.AppendLine($"public static {asyncModifier}{returnType} {interceptorMethodName}({parameters})")
              .AppendLine("{");

        using (source.Indent())
        {
            source.AppendLine($"var typedMessage = ({handler.MessageType.FullName})message;");

            // Generate the appropriate method call based on async/sync combinations
            if (isGeneric && handler.ReturnType.IsTuple)
            {
                // For generic calls with tuple return types, handle cascading messages inline for performance
                source.AppendLine($"var result = {(interceptorIsAsync && wrapperIsAsync ? "await " : "")}{stronglyTypedMethodName}(mediator, typedMessage, cancellationToken);")
                      .AppendLine();

                // Generate optimized typed code for tuple handling
                GenerateOptimizedTupleHandling(source, handler, expectedResponseTypeName, interceptorIsAsync);
            }
            else
            {
                string awaitKeyword = (interceptorIsAsync && wrapperIsAsync) ? "await " : "";
                string returnKeyword = isGeneric ? "return " : "";
                source.AppendLine($"{returnKeyword}{awaitKeyword}{stronglyTypedMethodName}(mediator, typedMessage, cancellationToken);");
            }
        }

        source.AppendLine("}");
    }

    private static string GenerateInterceptorAttribute(CallSiteInfo callSite)
    {
        var location = callSite.Location;
        return $"[global::System.Runtime.CompilerServices.InterceptsLocation({location.Version}, \"{location.Data}\")] // {location.DisplayLocation}";
    }

    private static string GenerateInterceptorReturnType(bool isAsync, bool isGeneric, string expectedResponseTypeName)
    {
        if (isGeneric)
        {
            // For generic methods, return the exact same type as the original method
            return isAsync ? $"global::System.Threading.Tasks.ValueTask<{expectedResponseTypeName}>" : expectedResponseTypeName;
        }

        // For non-generic methods, return the exact same type as the original method
        return isAsync ? "global::System.Threading.Tasks.ValueTask" : "void";
    }

    /// <summary>
    /// Gets a nullable-safe cast expression for HandlerResult.Value.
    /// </summary>
    private static string GetSafeCastExpression(string handlerResultVar, HandlerInfo handler, bool useUnwrappedType = false)
    {
        string returnType = useUnwrappedType || handler.ReturnType.IsVoid
            ? handler.ReturnType.UnwrappedFullName
            : handler.ReturnType.FullName;

        // Special handling for Result to Result<T> conversion
        if (returnType.StartsWith("Foundatio.Mediator.Result<") && returnType != "Foundatio.Mediator.Result")
        {
            // Check if the value might be a non-generic Result that needs conversion to Result<T>
            return $"{handlerResultVar}.Value is global::Foundatio.Mediator.Result result ? ({returnType})result : ({returnType}?){handlerResultVar}.Value ?? default({returnType})!";
        }

        // For reference types, provide a null-coalescing fallback to satisfy non-nullable return types
        if (handler.ReturnType.IsNullable)
        {
            string defaultValue = "default(" + returnType + ")";
            return $"({returnType}?){handlerResultVar}.Value ?? {defaultValue}";
        }

        return $"({returnType}){handlerResultVar}.Value!";
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

    private static string GenerateShortCircuitCheck(MiddlewareInfo middleware, string resultVariableName, string hrVariableName)
    {
        if (middleware.BeforeMethod == null)
            return $"if ({resultVariableName} is global::Foundatio.Mediator.HandlerResult {hrVariableName} && {hrVariableName}.IsShortCircuited)";

        var methodInfo = middleware.BeforeMethod.Value;

        // Use the return type for the check
        string returnType = methodInfo.ReturnType.FullName;

        // If the return type is exactly HandlerResult (not nullable), we can directly check it
        if (returnType == "HandlerResult" || returnType == "Foundatio.Mediator.HandlerResult")
        {
            return $"if ({resultVariableName}.IsShortCircuited)";
        }
        // If it's nullable HandlerResult, check for null first
        else if (returnType == "HandlerResult?" || returnType == "Foundatio.Mediator.HandlerResult?")
        {
            return $"if ({resultVariableName}?.IsShortCircuited == true)";
        }
        // Otherwise, fall back to pattern matching for object/object? return types
        else
        {
            return $"if ({resultVariableName} is global::Foundatio.Mediator.HandlerResult {hrVariableName} && {hrVariableName}.IsShortCircuited)";
        }
    }

    private static string GetHandlerResultValueExpression(MiddlewareInfo middleware, string resultVariableName, string hrVariableName, HandlerInfo handler, bool useUnwrappedType = false)
    {
        if (middleware.BeforeMethod == null)
            return GetSafeCastExpression(hrVariableName, handler, useUnwrappedType);

        var methodInfo = middleware.BeforeMethod.Value;

        // Use the return type
        string returnType = methodInfo.ReturnType.FullName;

        // If the return type is exactly HandlerResult (not nullable), we can directly access it
        if (returnType == "HandlerResult" || returnType == "Foundatio.Mediator.HandlerResult")
        {
            return GetSafeCastExpression(resultVariableName, handler, useUnwrappedType);
        }
        // If it's nullable HandlerResult, use the non-null assertion
        else if (returnType == "HandlerResult?" || returnType == "Foundatio.Mediator.HandlerResult?")
        {
            return GetSafeCastExpression($"{resultVariableName}!", handler, useUnwrappedType);
        }
        // Otherwise, fall back to using the pattern-matched variable
        else
        {
            return GetSafeCastExpression(hrVariableName, handler, useUnwrappedType);
        }
    }

    private static string GenerateMiddlewareMethodCall(MiddlewareInfo middleware, MiddlewareMethodInfo method, string parameters, string middlewareVariableName)
    {
        if (middleware.IsStatic)
        {
            return $"{middleware.FullName}.{method.MethodName}({parameters})";
        }
        else
        {
            return $"{middlewareVariableName}.{method.MethodName}({parameters})";
        }
    }

    private static void GenerateOptimizedTupleHandling(IndentedStringBuilder source, HandlerInfo handler, string expectedResponseTypeName, bool isAsync)
    {
        var tupleFields = handler.ReturnType.TupleItems.ToList();

        if (tupleFields.Count == 0)
        {
            source.AppendLine($"return default({expectedResponseTypeName});");
            return;
        }

        int returnItemIndex = -1;
        var publishItems = new List<int>();

        for (int i = 0; i < tupleFields.Count; i++)
        {
            var tupleItem = tupleFields[i];
            string fieldType = tupleItem.TypeFullName;

            if (fieldType == expectedResponseTypeName)
            {
                if (returnItemIndex == -1)
                {
                    returnItemIndex = i;
                }
                else
                {
                    publishItems.Add(i);
                }
            }
            else
            {
                publishItems.Add(i);
            }
        }

        foreach (int publishIndex in publishItems)
        {
            var tupleItem = tupleFields[publishIndex];
            string itemAccess = $"result.{tupleItem.Name}";

            bool needsNullCheck = tupleItem.IsNullable;

            if (isAsync)
            {
                if (needsNullCheck)
                {
                    source.AppendLine($"if ({itemAccess} != null) await mediator.PublishAsync({itemAccess}, cancellationToken);");
                }
                else
                {
                    source.AppendLine($"await mediator.PublishAsync({itemAccess}, cancellationToken);");
                }
            }
            else
            {
                if (needsNullCheck)
                {
                    source.AppendLine($"if ({itemAccess} != null) mediator.PublishAsync({itemAccess}, CancellationToken.None).GetAwaiter().GetResult();");
                }
                else
                {
                    source.AppendLine($"mediator.PublishAsync({itemAccess}, CancellationToken.None).GetAwaiter().GetResult();");
                }
            }
        }

        source.AppendLine();
        if (returnItemIndex >= 0)
        {
            var returnTupleItem = tupleFields[returnItemIndex];
            source.AppendLine($"return result.{returnTupleItem.Name};");
        }
        else
        {
            source.AppendLine($"return default({expectedResponseTypeName})!;");
        }
    }

    private static string GenerateMiddlewareParameterExpression(ParameterInfo parameter, MiddlewareInfo middleware, string resultVariableName, HandlerInfo handler)
    {
        if (parameter.IsMessageParameter)
            return "message";

        if (parameter.Type.IsCancellationToken)
            return "cancellationToken";

        // Check if it's exception parameter
        if (parameter.Type.FullName.Contains("Exception"))
            return "exception";

        // Check if it's the result from this middleware's Before method
        if (middleware.BeforeMethod?.ReturnType != null)
        {
            // If the Before method returns a simple (non-tuple) type, check direct match
            if (!middleware.BeforeMethod.Value.ReturnType.IsTuple)
            {
                if (parameter.Type.FullName == middleware.BeforeMethod.Value.ReturnType.FullName ||
                    parameter.Type.FullName == "object" || parameter.Type.FullName == "object?")
                    return resultVariableName;
            }
            else
            {
                // If the Before method returns a tuple, check if this parameter matches any tuple item
                var tupleItems = middleware.BeforeMethod.Value.ReturnType.TupleItems.ToList();
                for (int i = 0; i < tupleItems.Count; i++)
                {
                    if (parameter.Type.FullName == tupleItems[i].TypeFullName)
                    {
                        return $"{resultVariableName}.{tupleItems[i].Name}";
                    }
                }
            }
        }

        // Check if it's handler result parameter
        if (!handler.ReturnType.IsVoid && (parameter.Type.FullName == handler.ReturnType.FullName || parameter.Type.FullName == "object"))
            return "handlerResult";

        // Otherwise resolve from DI
        return $"serviceProvider.GetRequiredService<{parameter.Type.FullName}>()";
    }

    public static string GetHandlerClassName(HandlerInfo handler)
    {
        return $"{handler.Identifier}_{handler.MessageType.Identifier}_Wrapper";
    }

    public static string GetHandlerMethodName(HandlerInfo handler)
    {
        return handler.IsAsync ? "HandleAsync" : "Handle";
    }
}
