using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class HandlerGenerator
{
    public static void Execute(SourceProductionContext context, List<HandlerInfo> handlers, GeneratorConfiguration configuration)
    {
        if (handlers.Count == 0)
            return;

        Validate(context, handlers);

        foreach (var handler in handlers)
        {
            try
            {
                string wrapperClassName = GetHandlerClassName(handler);

                string source = GenerateHandler(handler, wrapperClassName, configuration);
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

    public static string GenerateHandler(HandlerInfo handler, string wrapperClassName, GeneratorConfiguration configuration)
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
            using Microsoft.Extensions.Logging;

            namespace Foundatio.Mediator.Generated;
            """);

        source.AppendLine();
        source.AddGeneratedCodeAttribute();
        source.AppendLine("[ExcludeFromCodeCoverage]");

        // Handler wrappers need to be public to support cross-assembly interceptors
        if (handler.IsGenericHandlerClass && handler.GenericArity > 0 && handler.GenericTypeParameters.Length == handler.GenericArity)
        {
            string genericParams = String.Join(", ", handler.GenericTypeParameters);
            source.AppendLine($"public static class {wrapperClassName}<{genericParams}>");
            source.IncrementIndent();
            if (handler.GenericConstraints.Length > 0)
            {
                foreach (string? c in handler.GenericConstraints)
                    source.AppendLine(c);
            }
            source.DecrementIndent();
        }
        else
        {
            source.AppendLine($"public static class {wrapperClassName}");
        }
        source.AppendLine("{");

        source.IncrementIndent();

        // Static cached logger - initialized once on first use
        source.AppendLine("private static ILogger? _logger;");
        source.AppendLine();

        GenerateHandleMethod(source, handler, configuration);

        GenerateUntypedHandleMethod(source, handler);

        GenerateInterceptorMethods(source, handler, configuration.InterceptorsEnabled);

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

    private static void GenerateHandleMethod(IndentedStringBuilder source, HandlerInfo handler, GeneratorConfiguration configuration)
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

        source.AppendLine($"public static {asyncModifier}{returnType} {stronglyTypedMethodName}(System.IServiceProvider serviceProvider, {handler.MessageType.FullName} message, System.Threading.CancellationToken cancellationToken)")
              .AppendLine("{");

        source.IncrementIndent();

        variables["System.IServiceProvider"] = "serviceProvider";
        source.AppendLine($"var logger = _logger ??= serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(\"{handler.FullName}\");");
        source.AppendLine($"logger?.LogProcessingMessage(\"{handler.MessageType.Identifier}\");");

        if (configuration.OpenTelemetryEnabled)
        {
            source.AppendLine();
            source.AppendLine($"using var activity = MediatorActivitySource.Instance.StartActivity(\"{handler.MessageType.Identifier}\");");
            source.AppendLine($"activity?.SetTag(\"messaging.system\", \"Foundatio.Mediator\");");
            source.AppendLine($"activity?.SetTag(\"messaging.message.type\", \"{handler.MessageType.FullName}\");");
        }

        source.AppendLine();

        // Check if any middleware needs HandlerExecutionInfo
        var needsHandlerInfo = handler.Middleware.Any(m =>
            (m.BeforeMethod?.Parameters.Any(p => p.Type.IsHandlerExecutionInfo) ?? false) ||
            (m.AfterMethod?.Parameters.Any(p => p.Type.IsHandlerExecutionInfo) ?? false) ||
            (m.FinallyMethod?.Parameters.Any(p => p.Type.IsHandlerExecutionInfo) ?? false));

        // Create HandlerExecutionInfo for middleware only if needed
        if (needsHandlerInfo)
        {
            // Build parameter types array for GetMethod to handle overloaded methods
            var paramTypes = string.Join(", ", handler.Parameters.Select(p => $"typeof({p.Type.FullName})"));
            if (string.IsNullOrEmpty(paramTypes))
            {
                source.AppendLine($"var handlerExecutionInfo = new Foundatio.Mediator.HandlerExecutionInfo(typeof({handler.FullName}), typeof({handler.FullName}).GetMethod(\"{handler.MethodName}\", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance, null, System.Type.EmptyTypes, null)!);");
            }
            else
            {
                source.AppendLine($"var handlerExecutionInfo = new Foundatio.Mediator.HandlerExecutionInfo(typeof({handler.FullName}), typeof({handler.FullName}).GetMethod(\"{handler.MethodName}\", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance, null, new[] {{ {paramTypes} }}, null)!);");
            }
            variables[WellKnownTypes.HandlerExecutionInfo] = "handlerExecutionInfo";
            source.AppendLine();
        }

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
        source.AppendLineIf($"{handler.ReturnType.UnwrappedFullName}{(allowNull ? "?" : "")} handlerResult = default;", handler.HasReturnValue);

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
                string shortCircuitValue = "";
                if (handler.ReturnType.IsResult)
                {
                    shortCircuitValue = $"{m.Middleware.Identifier.ToCamelCase()}Result.Value is Foundatio.Mediator.Result result ? ({handler.ReturnType.UnwrappedFullName})result : ({handler.ReturnType.UnwrappedFullName}?){m.Middleware.Identifier.ToCamelCase()}Result.Value!";
                }
                else if (handler.ReturnType.IsTuple)
                {
                    shortCircuitValue = $"(({m.Middleware.Identifier.ToCamelCase()}Result.Value is Foundatio.Mediator.Result result ? ({handler.ReturnType.TupleItems.First().TypeFullName})result : ({handler.ReturnType.TupleItems.First().TypeFullName}?){m.Middleware.Identifier.ToCamelCase()}Result.Value!), {String.Join(", ", handler.ReturnType.TupleItems.Skip(1).Select(i => i.IsNullable ? "null" : "default"))})";
                }
                else
                {
                    shortCircuitValue = $"({handler.ReturnType.UnwrappedFullName}){m.Middleware.Identifier.ToCamelCase()}Result.Value!";
                }

                source.AppendLine($"if ({m.Middleware.Identifier.ToCamelCase()}Result.IsShortCircuited)");
                source.AppendLine("{");
                source.AppendLine($"    logger?.LogShortCircuitedMessage(\"{handler.MessageType.Identifier}\");");

                if (handler.HasReturnValue)
                {
                    source.AppendLine($"    handlerResult = {shortCircuitValue};");
                    source.AppendLine("    return handlerResult;");
                }
                else
                {
                    source.AppendLine("    return;");
                }

                source.AppendLine("}");
            }
        }
        source.AppendLineIf(beforeMiddleware.Any());

        // call handler
        asyncModifier = handler.ReturnType.IsTask ? "await " : "";
        result = handler.ReturnType.IsVoid ? "" : "handlerResult = ";
        accessor = handler.IsStatic ? handler.FullName : $"handlerInstance";
        parameters = BuildParameters(source, handler.Parameters, variables);

        source.AppendLineIf("var handlerInstance = GetOrCreateHandler(serviceProvider);", !handler.IsStatic);
        source.AppendLine($"{result}{asyncModifier}{accessor}.{handler.MethodName}({parameters});");
        source.AppendLineIf(handler.HasReturnValue);

        if (handler.HasReturnValue)
        {
            variables[handler.ReturnType.FullName] = "handlerResult";

            if (handler.ReturnType.IsResult)
            {
                variables[WellKnownTypes.Result] = "handlerResult!";
            }

            if (handler.ReturnType.IsTuple)
            {
                foreach (var tupleItem in handler.ReturnType.TupleItems)
                {
                    variables[tupleItem.TypeFullName] = $"handlerResult.{tupleItem.Name}";

                    if (tupleItem.TypeFullName.StartsWith(WellKnownTypes.ResultOfT.Replace("`1", "<")))
                    {
                        variables[WellKnownTypes.Result] = $"handlerResult.{tupleItem.Name}!";
                    }
                }
            }
        }

        // call after middleware
        foreach (var m in afterMiddleware)
        {
            asyncModifier = m.Method.IsAsync ? "await " : "";
            accessor = m.Middleware.IsStatic ? m.Middleware.FullName : $"{m.Middleware.Identifier.ToCamelCase()}";
            parameters = BuildParameters(source, m.Method.Parameters, variables);

            source.AppendLine($"{asyncModifier}{accessor}.{m.Method.MethodName}({parameters});");
        }
        source.AppendLineIf(afterMiddleware.Any());

        source.AppendLineIf($"logger?.LogCompletedMessage(\"{handler.MessageType.Identifier}\");", !shouldUseTryCatch);
        if (configuration.OpenTelemetryEnabled && !shouldUseTryCatch)
        {
            source.AppendLine("activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);");
        }
        if (handler.HasReturnValue)
        {
            source.AppendLine("return handlerResult;");
        }

        if (shouldUseTryCatch)
        {
            source.DecrementIndent();

            source.AppendLine("""
                }
                catch (Exception ex)
                {
                    exception = ex;
                """);

            if (configuration.OpenTelemetryEnabled)
            {
                source.AppendLine("    activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);");
                source.AppendLine("    activity?.SetTag(\"exception.type\", ex.GetType().FullName);");
                source.AppendLine("    activity?.SetTag(\"exception.message\", ex.Message);");
            }

            source.AppendLine("""
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

            if (configuration.OpenTelemetryEnabled)
            {
                source.AppendLine("activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);");
            }
            source.AppendLine($"logger?.LogCompletedMessage(\"{handler.MessageType.Identifier}\");");
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
        source.IncrementIndent();

        // For handlers that can use fast path, skip scope creation entirely
        bool canUseFastPath = handler.CanUseZeroAllocFastPath || handler.CanUseSingletonFastPath;
        string serviceProviderExpr;

        if (canUseFastPath)
        {
            // Cast mediator directly to IServiceProvider - no scope needed
            serviceProviderExpr = "(System.IServiceProvider)mediator";
        }
        else if (handler.IsAsync)
        {
            source.AppendLine("await using var scopedMediator = await ScopedMediator.GetOrCreateAsync(mediator);");
            serviceProviderExpr = "scopedMediator.Services";
        }
        else
        {
            source.AppendLine("using var scopedMediator = ScopedMediator.GetOrCreate(mediator);");
            serviceProviderExpr = "scopedMediator.Services";
        }

        source.AppendLine($"var typedMessage = ({handler.MessageType.FullName})message;");

        string stronglyTypedMethodName = GetHandlerMethodName(handler);
        string asyncModifier = handler.IsAsync ? "await " : "";
        string result = handler.ReturnType.IsVoid ? "" : "var result = ";

        source.AppendLine($"{result}{asyncModifier}{stronglyTypedMethodName}({serviceProviderExpr}, typedMessage, cancellationToken);");

        if (handler.ReturnType.IsTuple)
        {
            // Cascading messages need the mediator for publishing
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

        source.DecrementIndent();
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

        // Build handler call arguments - only include CancellationToken if handler accepts it
        bool hasCancellationToken = handler.Parameters.Any(p => p.Type.IsCancellationToken);
        string handlerArgs = hasCancellationToken ? "typedMessage, cancellationToken" : "typedMessage";
        bool needsValueTaskWrap = methodIsAsync && !handler.IsAsync;

        // Zero-alloc fast path for static handlers: skip scope creation and call handler directly
        // This applies when handler is static, has no DI method parameters, no middleware, and no cascading messages
        if (handler.CanUseZeroAllocFastPath)
        {
            source.AppendLine($"var typedMessage = ({handler.MessageType.FullName})message;");

            asyncModifier = handler.IsAsync ? "await " : "";

            // Static handler: call directly
            if (responseType.IsVoid)
            {
                source.AppendLine($"{asyncModifier}{handler.FullName}.{handler.MethodName}({handlerArgs});");

                if (needsValueTaskWrap)
                {
                    source.AppendLine("return System.Threading.Tasks.ValueTask.CompletedTask;");
                }
            }
            else
            {
                if (needsValueTaskWrap)
                {
                    source.AppendLine($"return new System.Threading.Tasks.ValueTask<{responseType.UnwrappedFullName}>({handler.FullName}.{handler.MethodName}({handlerArgs}));");
                }
                else
                {
                    source.AppendLine($"return {asyncModifier}{handler.FullName}.{handler.MethodName}({handlerArgs});");
                }
            }
        }
        // Singleton fast path: resolve handler from root service provider without creating a scope
        // GetOrCreateHandler caches the handler instance since CanUseSingletonFastPath is true
        // WARNING: This will behave incorrectly if the handler is registered as Scoped!
        else if (handler.CanUseSingletonFastPath)
        {
            source.AppendLine($"var typedMessage = ({handler.MessageType.FullName})message;");
            source.AppendLine($"var handlerInstance = GetOrCreateHandler((System.IServiceProvider)mediator);");

            asyncModifier = handler.IsAsync ? "await " : "";

            if (responseType.IsVoid)
            {
                source.AppendLine($"{asyncModifier}handlerInstance.{handler.MethodName}({handlerArgs});");

                if (needsValueTaskWrap)
                {
                    source.AppendLine("return System.Threading.Tasks.ValueTask.CompletedTask;");
                }
            }
            else
            {
                if (needsValueTaskWrap)
                {
                    source.AppendLine($"return new System.Threading.Tasks.ValueTask<{responseType.UnwrappedFullName}>(handlerInstance.{handler.MethodName}({handlerArgs}));");
                }
                else
                {
                    source.AppendLine($"return {asyncModifier}handlerInstance.{handler.MethodName}({handlerArgs});");
                }
            }
        }
        else
        {
            // Standard path: create scope and go through HandleAsync
            if (handler.IsAsync)
            {
                source.AppendLine("await using var scopedMediator = await ScopedMediator.GetOrCreateAsync(mediator);");
            }
            else
            {
                source.AppendLine("using var scopedMediator = ScopedMediator.GetOrCreate(mediator);");
            }

            source.AppendLine($"var typedMessage = ({handler.MessageType.FullName})message;");

            asyncModifier = handler.IsAsync ? "await " : "";
            if (handler.ReturnType.IsTuple)
            {
                source.AppendLine($"var result = {asyncModifier}{handlerMethod}(scopedMediator.Services, typedMessage, cancellationToken);");
                source.AppendLine();

                var returnItem = handler.ReturnType.TupleItems.FirstOrDefault(i => i.TypeFullName == responseType.FullName);
                if (returnItem == default)
                {
                    returnItem = handler.ReturnType.TupleItems.First();
                }
                var publishItems = handler.ReturnType.TupleItems.Except([returnItem]);

                foreach (var publishItem in publishItems)
                {
                    source.AppendLineIf($"if (result.{publishItem.Name} != null)", publishItem.IsNullable);
                    source.AppendIf("    ", publishItem.IsNullable).AppendLine($"await scopedMediator.PublishAsync(result.{publishItem.Name}, cancellationToken);");
                }
                source.AppendLineIf(publishItems.Any());

                source.AppendLine($"return result.{returnItem.Name};");
            }
            else
            {
                if (responseType.IsVoid)
                {
                    source.AppendLine($"{asyncModifier}{handlerMethod}(scopedMediator.Services, typedMessage, cancellationToken);");

                    if (needsValueTaskWrap)
                    {
                        source.AppendLine("return System.Threading.Tasks.ValueTask.CompletedTask;");
                    }
                }
                else
                {
                    if (needsValueTaskWrap)
                    {
                        source.AppendLine($"return new System.Threading.Tasks.ValueTask<{responseType.UnwrappedFullName}>({handlerMethod}(scopedMediator.Services, typedMessage, cancellationToken));");
                    }
                    else
                    {
                        source.AppendLine($"return {asyncModifier}{handlerMethod}(scopedMediator.Services, typedMessage, cancellationToken);");
                    }
                }
            }
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
            else if (param.Type.IsHandlerExecutionInfo)
            {
                parameterValues.Add("handlerExecutionInfo");
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
            else if (param.Type.IsResult && param.Type.FullName == WellKnownTypes.Result && variables != null && variables.TryGetValue(WellKnownTypes.Result, out string? resultVariableName))
            {
                // Special case: parameter is base Result type, but a Result<T> is available
                // The null-forgiving operator is already added when storing in variables
                parameterValues.Add(resultVariableName);
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
                """);
    }



    private static void GenerateGetOrCreateHandler(IndentedStringBuilder source, HandlerInfo handler)
    {
        // For handlers that can use singleton fast path, cache a directly-instantiated instance
        // This is safe because CanUseSingletonFastPath ensures no constructor parameters
        if (handler.CanUseSingletonFastPath)
        {
            source.AppendLine()
                  .AppendLines($$"""
                    private static readonly {{handler.FullName}} _cachedHandler = new();

                    [DebuggerStepThrough]
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    private static {{handler.FullName}} GetOrCreateHandler(IServiceProvider serviceProvider)
                    {
                        return _cachedHandler;
                    }
                    """);
        }
        else
        {
            source.AppendLine()
                  .AppendLines($$"""
                    [DebuggerStepThrough]
                    private static {{handler.FullName}} GetOrCreateHandler(IServiceProvider serviceProvider)
                    {
                        return serviceProvider.GetRequiredService<{{handler.FullName}}>();
                    }
                    """);
        }
    }

    public static string GetHandlerClassName(HandlerInfo handler)
    {
        return $"{handler.Identifier}_{handler.MessageType.Identifier}_Handler";
    }

    public static string GetHandlerFullName(HandlerInfo handler, string? handlerNamespace = null, string? assemblyName = null)
    {
        // Handler wrappers are always generated in Foundatio.Mediator.Generated namespace
        // The handlerNamespace and assemblyName parameters are reserved for future use
        return $"Foundatio.Mediator.Generated.{GetHandlerClassName(handler)}";
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
            handlersForMessage ??= [];

            foreach (var callSite in callSites)
            {
                // No cross-assembly handlers in this context (local validation only)
                ValidateCallSite(context, callSite, handlersForMessage, handlersForMessage, []);
            }
        }
    }

    // Global validation that considers all call sites discovered (including those without matching handlers)
    public static void ValidateGlobalCallSites(
        SourceProductionContext context,
        List<HandlerInfo> handlers,
        System.Collections.Immutable.ImmutableArray<CallSiteInfo> allDiscoveredCallSites,
        List<HandlerInfo>? crossAssemblyHandlers = null)
    {
        crossAssemblyHandlers ??= [];

        // Group local handlers by message type for validation
        var localHandlersByMessageType = handlers
            .GroupBy(h => h.MessageType.FullName)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Group cross-assembly handlers by message type
        var crossAssemblyHandlersByMessageType = crossAssemblyHandlers
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

            // Get local and cross-assembly handlers for this message type
            localHandlersByMessageType.TryGetValue(messageTypeName, out var localHandlers);
            localHandlers ??= [];

            crossAssemblyHandlersByMessageType.TryGetValue(messageTypeName, out var externalHandlers);
            externalHandlers ??= [];

            // Combine all handlers for validation
            var allHandlersForMessage = localHandlers.Concat(externalHandlers).ToList();

            foreach (var callSite in callSites)
            {
                ValidateCallSite(context, callSite, allHandlersForMessage, localHandlers, externalHandlers);
            }
        }
    }

    private static void ValidateCallSite(
        SourceProductionContext context,
        CallSiteInfo callSite,
        List<HandlerInfo> allHandlersForMessage,
        List<HandlerInfo> localHandlers,
        List<HandlerInfo> crossAssemblyHandlers)
    {
        bool isInvokeCall = callSite.MethodName is "Invoke" or "InvokeAsync";

        if (!isInvokeCall)
            return; // Only validate Invoke calls, not Publish

        // If the message is a generic type parameter (e.g., T), we cannot know the handler at compile time,
        // so do not emit FMED007 for missing/multiple handlers.
        if (callSite.MessageType.IsTypeParameter)
            return;

        // FMED007: Multiple handlers found for invoke call (includes both local and cross-assembly)
        if (allHandlersForMessage.Count > 1)
        {
            var localNames = localHandlers.Select(h => h.FullName);
            var externalNames = crossAssemblyHandlers.Select(h => $"{h.FullName} (referenced assembly)");
            var handlerNames = string.Join(", ", localNames.Concat(externalNames));
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

        // No handler found - skip validation since we can't validate without a handler
        if (allHandlersForMessage.Count == 0)
            return;

        var handler = allHandlersForMessage[0];
        bool isAsyncCall = callSite.MethodName == "InvokeAsync";
        bool isCrossAssemblyHandler = crossAssemblyHandlers.Count > 0;

        // Evaluate specific async characteristics for precise diagnostics
        bool returnsTask = handler.ReturnType.IsTask;
        bool returnsTuple = handler.ReturnType.IsTuple;
        // Note: Cross-assembly handlers don't have middleware info at this point
        bool hasAsyncMiddleware = handler.Middleware.Any(m => m.IsAsync);

        // Prefer the most specific diagnostics first when using synchronous Invoke
        if (!isAsyncCall)
        {
            // FMED010: Sync invoke on handler that returns tuple
            if (returnsTuple)
            {
                string handlerLocation = isCrossAssemblyHandler ? " in referenced assembly" : "";
                var diagnostic = new DiagnosticInfo
                {
                    Identifier = "FMED010",
                    Title = "Synchronous invoke on handler with tuple return type",
                    Message = $"Cannot use synchronous 'Invoke' on handler '{handler.FullName}'{handlerLocation} that returns a tuple. Use 'InvokeAsync' instead because cascading messages need to be published asynchronously.",
                    Severity = DiagnosticSeverity.Error,
                    Location = callSite.Location
                };
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
                return;
            }

            // FMED009: Sync invoke on handler with async middleware (only applies to local handlers with middleware info)
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
                string handlerLocation = isCrossAssemblyHandler ? " in referenced assembly" : "";
                var diagnostic = new DiagnosticInfo
                {
                    Identifier = "FMED008",
                    Title = "Synchronous invoke on asynchronous handler",
                    Message = $"Cannot use synchronous 'Invoke' on asynchronous handler '{handler.FullName}'{handlerLocation}. Use 'InvokeAsync' instead.",
                    Severity = DiagnosticSeverity.Error,
                    Location = callSite.Location
                };
                context.ReportDiagnostic(diagnostic.ToDiagnostic());
                return;
            }
        }
    }
}
