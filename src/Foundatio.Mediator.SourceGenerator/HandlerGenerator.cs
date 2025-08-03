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

        GenerateUntypedHandleMethod(source, handler);

        GenerateInterceptorMethods(source, handler);

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
            parameters = BuildParameters(m.Method.Parameters);

            source.AppendLine($"{result}{asyncModifier}{accessor}.{m.Method.MethodName}({parameters});");
        }
        source.AppendLineIf(beforeMiddleware.Any());

        // call handler
        asyncModifier = handler.ReturnType.IsTask ? "await " : "";
        result = handler.ReturnType.IsVoid ? "" : "handlerResult = ";
        accessor = handler.IsStatic ? handler.FullName : $"handlerInstance";
        parameters = BuildParameters(handler.Parameters);

        source.AppendLineIf("var handlerInstance = GetOrCreateHandler(serviceProvider);", !handler.IsStatic);
        source.AppendLine($"{result}{asyncModifier}{accessor}.{handler.MethodName}({parameters});");
        source.AppendLineIf(handler.HasReturnValue);

        // call after middleware
        foreach (var m in afterMiddleware)
        {
            asyncModifier = m.Method.IsAsync ? "await " : "";
            accessor = m.Middleware.IsStatic ? m.Middleware.FullName : $"{m.Middleware.Identifier.ToCamelCase()}";
            parameters = BuildParameters(m.Method.Parameters, variables);

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
                parameters = BuildParameters(m.Method.Parameters, variables);

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

    private static void GenerateInterceptorMethods(IndentedStringBuilder source, HandlerInfo handler)
    {
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

        foreach (var callSite in callSites)
        {
            source.AppendLine($"[System.Runtime.CompilerServices.InterceptsLocation({callSite.Location.Version}, \"{callSite.Location.Data}\")] // {callSite.Location.DisplayLocation}");
        }

        string asyncModifier = responseType.IsTask ? "async " : "";
        string parameters = "this Foundatio.Mediator.IMediator mediator, object message, System.Threading.CancellationToken cancellationToken = default";
        source.AppendLine($"public static {asyncModifier}{responseType.FullName} {interceptorMethod}({parameters})");
        source.AppendLine("{");

        source.IncrementIndent();

        source.AppendLine($"var typedMessage = ({handler.MessageType.FullName})message;");

        asyncModifier = responseType.IsTask ? "await " : "";
        if (handler.ReturnType.IsTuple)
        {
            source.AppendLine($"var result = {asyncModifier}{handlerMethod}(mediator, typedMessage, cancellationToken);");
            source.AppendLine();

            GenerateOptimizedTupleHandling(source, handler, responseType);
        }
        else
        {
            string returnKeyword = responseType.IsVoid ? "" : "return ";
            source.AppendLine($"{returnKeyword}{asyncModifier}{handlerMethod}(mediator, typedMessage, cancellationToken);");
        }

        source.DecrementIndent();

        source.AppendLine("}");
    }

    private static string BuildParameters(EquatableArray<ParameterInfo> parameters, Dictionary<string, string>? variables = null)
    {
        var parameterValues = new List<string>();

        foreach (var param in parameters)
        {
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

    private static string GenerateShortCircuitCheck(MiddlewareInfo middleware, string resultVariableName, string hrVariableName)
    {
        if (middleware.BeforeMethod == null)
            return $"if ({resultVariableName} is Foundatio.Mediator.HandlerResult {hrVariableName} && {hrVariableName}.IsShortCircuited)";

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
            return $"if ({resultVariableName} is Foundatio.Mediator.HandlerResult {hrVariableName} && {hrVariableName}.IsShortCircuited)";
        }
    }

    private static void GenerateOptimizedTupleHandling(IndentedStringBuilder source, HandlerInfo handler, TypeSymbolInfo responseType)
    {
        var tupleFields = handler.ReturnType.TupleItems.ToList();

        if (tupleFields.Count == 0)
        {
            source.AppendLine($"return default({responseType.FullName});");
            return;
        }

        int returnItemIndex = -1;
        var publishItems = new List<int>();

        for (int i = 0; i < tupleFields.Count; i++)
        {
            var tupleItem = tupleFields[i];
            string fieldType = tupleItem.TypeFullName;

            if (fieldType == responseType.FullName)
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

            if (responseType.IsTask)
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
            source.AppendLine($"return default({responseType.UnwrappedFullName})!;");
        }
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
