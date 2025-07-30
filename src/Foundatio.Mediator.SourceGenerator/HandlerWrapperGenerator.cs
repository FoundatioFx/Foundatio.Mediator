using Microsoft.CodeAnalysis;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class HandlerWrapperGenerator
{
    public static void GenerateHandlerWrappers(List<HandlerInfo> handlers, List<MiddlewareInfo> middlewares, bool interceptorsEnabled, SourceProductionContext context)
    {
        if (handlers == null || handlers.Count == 0)
            return;

        foreach (var handler in handlers)
        {
            try
            {
                string wrapperClassName = GetWrapperClassName(handler);
                var applicableMiddlewares = GetApplicableMiddlewares(middlewares ?? [], handler);

                string source = GenerateHandlerWrapper(handler, wrapperClassName, applicableMiddlewares, interceptorsEnabled);
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
                        $"Error generating wrapper for handler {handler.HandlerTypeName}: {ex.Message}\nStackTrace: {ex.StackTrace}",
                        "Generator",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    public static string GenerateHandlerWrapper(HandlerInfo handler, string wrapperClassName, List<MiddlewareInfo> middlewares, bool interceptorsEnabled)
    {
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader();

        source.AppendLine("using System;")
              .AppendLine("using System.Collections.Generic;")
              .AppendLine("using System.Diagnostics;")
              .AppendLine("using System.Diagnostics.CodeAnalysis;")
              .AppendLine("using System.Linq;")
              .AppendLine("using System.Reflection;")
              .AppendLine("using System.Runtime.CompilerServices;")
              .AppendLine("using System.Threading;")
              .AppendLine("using System.Threading.Tasks;")
              .AppendLine("using Microsoft.Extensions.DependencyInjection;")
              .AppendLine()
              .AppendLine("namespace Foundatio.Mediator;")
              .AppendLine()
              .AppendLine("[ExcludeFromCodeCoverage]")
              .AppendLine($"internal static class {wrapperClassName}")
              .AppendLine("{");

        using (source.Indent())
        {
            // Generate strongly typed method that matches handler signature
            GenerateStronglyTypedMethod(source, handler, middlewares);

            bool hasAsyncMiddleware = middlewares.Any(m => m.IsAsync);
            bool needsAsyncHandleMethod = handler.IsAsync || hasAsyncMiddleware;
            if (needsAsyncHandleMethod)
            {
                GenerateAsyncHandleMethod(source, handler);
            }
            else
            {
                GenerateSyncHandleMethod(source, handler);
            }

            var callSites = handler.CallSites.ToList();
            if (interceptorsEnabled && callSites.Count > 0)
            {
                GenerateInterceptorMethods(source, handler, callSites, middlewares);
            }

            if (!handler.IsStatic)
            {
                GenerateGetOrCreateHandler(source, handler);
            }

            if (handler.HasReturnValue && handler.ReturnTypeIsTuple)
            {
                GeneratePublishCascadingMessages(source);
            }
        }

        source.AppendLine("}");

        return source.ToString();
    }

    public static string GetWrapperClassName(HandlerInfo handler)
    {
        string handlerTypeName = Helpers.GetSimpleTypeName(handler.HandlerTypeName);
        string messageTypeName = Helpers.GetSimpleTypeName(handler.MessageTypeName);
        return $"{handlerTypeName}_{messageTypeName}_Wrapper";
    }

    public static string GetStronglyTypedMethodName(HandlerInfo handler)
    {
        return handler.IsAsync ? "HandleAsync" : "Handle";
    }

    private static void GenerateStronglyTypedMethod(IndentedStringBuilder source, HandlerInfo handler, List<MiddlewareInfo> middlewares)
    {
        string stronglyTypedMethodName = GetStronglyTypedMethodName(handler);

        // For the strongly typed method, we need to preserve the original method signature
        // but make it async if we have async middleware or the handler is async
        bool hasAsyncMiddleware = middlewares.Any(m => m.IsAsync);

        string returnType = ReconstructOriginalReturnType(handler, hasAsyncMiddleware);
        bool isAsync = handler.IsAsync || hasAsyncMiddleware;

        string asyncModifier = isAsync ? "async " : "";

        source.AppendLine($"public static {asyncModifier}{returnType} {stronglyTypedMethodName}(global::Foundatio.Mediator.IMediator mediator, {handler.MessageTypeName} message, CancellationToken cancellationToken)")
              .AppendLine("{");

        using (source.Indent())
        {
            source.AppendLine("var serviceProvider = (IServiceProvider)mediator;");

            if (middlewares.Any())
            {
                // Generate middleware-aware execution
                GenerateMiddlewareAwareExecution(source, handler, middlewares, stronglyTypedMethodName);
            }
            else
            {
                // Generate method call based on whether the handler method is static or instance
                string methodCall;
                if (handler.IsStatic)
                {
                    // For static methods, call directly on the handler type
                    methodCall = GenerateStaticMethodCall(handler, "message", "cancellationToken", "serviceProvider");
                }
                else
                {
                    // For instance methods, get handler instance first
                    source.AppendLine("var handlerInstance = GetOrCreateHandler(serviceProvider);");
                    methodCall = GenerateMethodCall(handler, "handlerInstance", "message", "cancellationToken");
                }

                // Handle the return based on the original return type
                if (!handler.HasReturnValue || handler.ReturnTypeName == "void")
                {
                    // For void methods, call the method without returning anything
                    if (handler.IsAsync)
                    {
                        source.AppendLine($"await {methodCall};");
                    }
                    else
                    {
                        source.AppendLine($"{methodCall};");
                    }
                }
                else
                {
                    // For all other cases, just return the result directly
                    if (handler.IsAsync)
                    {
                        source.AppendLine($"return await {methodCall};");
                    }
                    else
                    {
                        source.AppendLine($"return {methodCall};");
                    }
                }
            }
        }

        source.AppendLine("}");

        source.AppendLine();
    }

    private static string ReconstructOriginalReturnType(HandlerInfo handler, bool hasAsyncMiddleware = false)
    {
        // Use the original return type directly from the handler method
        string originalReturnType = handler.OriginalReturnTypeName ?? "void";

        // If the handler is already async, preserve its return type
        if (handler.IsAsync)
        {
            return originalReturnType;
        }

        // If we have async middleware, we need to make the wrapper async
        if (hasAsyncMiddleware)
        {
            // For sync handlers with async middleware, we need to convert return types to async
            if (originalReturnType == "void")
            {
                return "System.Threading.Tasks.Task";
            }
            else
            {
                return $"System.Threading.Tasks.Task<{originalReturnType}>";
            }
        }

        // For sync handlers without async middleware, keep the original return type as-is
        return originalReturnType;
    }

    private static string GenerateMethodCall(HandlerInfo handler, string handlerVariable, string messageVariable, string cancellationTokenVariable)
    {
        var parameters = new List<string>();

        foreach (var param in handler.Parameters)
        {
            if (param.IsMessage)
            {
                parameters.Add(messageVariable);
            }
            else if (param.IsCancellationToken)
            {
                parameters.Add(cancellationTokenVariable);
            }
            else
            {
                // This is a dependency that needs to be resolved from DI
                parameters.Add($"serviceProvider.GetRequiredService<{param.TypeName}>()");
            }
        }

        string parameterList = String.Join(", ", parameters);
        return $"{handlerVariable}.{handler.MethodName}({parameterList})";
    }

    private static string GenerateStaticMethodCall(HandlerInfo handler, string messageVariable, string cancellationTokenVariable, string serviceProviderVariable)
    {
        var parameters = new List<string>();

        foreach (var param in handler.Parameters)
        {
            if (param.IsMessage)
            {
                parameters.Add(messageVariable);
            }
            else if (param.IsCancellationToken)
            {
                parameters.Add(cancellationTokenVariable);
            }
            else
            {
                // This is a dependency that needs to be resolved from DI
                parameters.Add($"{serviceProviderVariable}.GetRequiredService<{param.TypeName}>()");
            }
        }

        string parameterList = String.Join(", ", parameters);
        return $"{handler.HandlerTypeName}.{handler.MethodName}({parameterList})";
    }

    private static void GenerateAsyncHandleMethod(IndentedStringBuilder source, HandlerInfo handler)
    {
        GenerateUntypedHandleMethod(source, handler, isAsync: true);
    }

    private static void GenerateSyncHandleMethod(IndentedStringBuilder source, HandlerInfo handler)
    {
        GenerateUntypedHandleMethod(source, handler, isAsync: false);
    }

    private static void GenerateUntypedHandleMethod(IndentedStringBuilder source, HandlerInfo handler, bool isAsync)
    {
        if (isAsync)
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
            source.AppendLine($"var typedMessage = ({handler.MessageTypeName})message;");

            string stronglyTypedMethodName = GetStronglyTypedMethodName(handler);

            if (handler.HasReturnValue)
            {
                source.AppendLine($"var result = {(isAsync ? "await " : "")}{stronglyTypedMethodName}(mediator, typedMessage, cancellationToken);");

                if (handler.ReturnTypeIsTuple)
                {
                    source.AppendLine("return await PublishCascadingMessagesAsync(mediator, result, responseType);");
                }
                else
                {
                    GenerateNonTupleResultHandling(source, handler);
                }
            }
            else
            {
                source.AppendLine($"{(isAsync ? "await " : "")}{stronglyTypedMethodName}(mediator, typedMessage, cancellationToken);");
                source.AppendLine();
                source.AppendLine("return null;");
            }
        }

        source.AppendLine("}");
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

        if (handler.ReturnTypeIsResult)
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

        if (handler.HasReturnValue)
            source.AppendLine("return result;");
        else
            source.AppendLine("return null;");
    }

    private static void GenerateInterceptorMethods(IndentedStringBuilder source, HandlerInfo handler, List<CallSiteInfo> callSites, List<MiddlewareInfo> middlewares)
    {
        // Group call sites by method signature to generate unique interceptor methods
        var callSiteGroups = callSites
            .GroupBy(cs => new { cs.MethodName, cs.MessageTypeName, cs.ResponseTypeName })
            .ToList();

        int methodCounter = 0;
        foreach (var group in callSiteGroups)
        {
            var key = group.Key;
            var groupCallSites = group.ToList();

            GenerateInterceptorMethod(source, handler, key.MethodName, key.ResponseTypeName ?? "", groupCallSites, methodCounter++, middlewares);
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
        string stronglyTypedMethodName = GetStronglyTypedMethodName(handler);

        string asyncModifier = interceptorIsAsync ? "async " : "";
        source.AppendLine($"public static {asyncModifier}{returnType} {interceptorMethodName}({parameters})")
              .AppendLine("{");

        using (source.Indent())
        {
            source.AppendLine($"var typedMessage = ({handler.MessageTypeName})message;");

            // Generate the appropriate method call based on async/sync combinations
            if (isGeneric && handler.ReturnTypeIsTuple)
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
        var location = callSite.InterceptableLocation;
        return $"[global::System.Runtime.CompilerServices.InterceptsLocation({location.Version}, \"{location.Data}\")] // {location.GetDisplayLocation()}";
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

    private static List<MiddlewareInfo> GetApplicableMiddlewares(List<MiddlewareInfo> middlewares, HandlerInfo handler)
    {
        var applicable = new List<MiddlewareInfo>();

        foreach (var middleware in middlewares)
        {
            if (IsMiddlewareApplicableToHandler(middleware, handler))
            {
                applicable.Add(middleware);
            }
        }

        // Sort by priority: message-specific first, then interface-based, then object-based
        // Within each priority level, sort by Order attribute
        return applicable
            .OrderBy(m => m.IsObjectType ? 2 : (m.IsInterfaceType ? 1 : 0)) // Priority: specific=0, interface=1, object=2
            .ThenBy(m => m.Order)
            .ToList();
    }

    private static bool IsMiddlewareApplicableToHandler(MiddlewareInfo middleware, HandlerInfo handler)
    {
        // Check if this middleware applies to the handler
        if (middleware.IsObjectType)
        {
            // Object-type middleware applies to all handlers
            return true;
        }

        if (middleware.MessageTypeName == handler.MessageTypeName)
        {
            // Direct message type match
            return true;
        }

        if (middleware.IsInterfaceType && middleware.InterfaceTypes.Contains(handler.MessageTypeName))
        {
            // Handler's message type implements the middleware's interface
            return true;
        }

        return false;
    }

    private static void GenerateMiddlewareAwareExecution(IndentedStringBuilder source, HandlerInfo handler, List<MiddlewareInfo> applicableMiddlewares, string methodName)
    {
        // Determine if we need async execution based on handler or any async middleware
        bool hasAsyncMiddleware = applicableMiddlewares.Any(m => m.IsAsync);
        bool needsAsync = handler.IsAsync || hasAsyncMiddleware;

        // Check compatibility for all middleware
        foreach (var middleware in applicableMiddlewares)
        {
            if (!IsMiddlewareCompatibleWithHandler(middleware, handler))
            {
                // Generate error or fallback to direct handler execution
                GenerateDirectHandlerExecution(source, handler);
                return;
            }
        }

        // Use unified middleware execution for both single and multiple middleware cases
        GenerateMiddlewareExecutionCore(source, handler, applicableMiddlewares, needsAsync);
    }

    private static bool IsMiddlewareCompatibleWithHandler(MiddlewareInfo middleware, HandlerInfo handler)
    {
        // If handler is sync, middleware must have sync methods or be async-compatible
        if (!handler.IsAsync)
        {
            // Check if middleware has sync Before/After/Finally methods
            return (middleware.BeforeMethod != null && !middleware.BeforeMethod.Value.IsAsync) ||
                   (middleware.AfterMethod != null && !middleware.AfterMethod.Value.IsAsync) ||
                   (middleware.FinallyMethod != null && !middleware.FinallyMethod.Value.IsAsync) ||
                   middleware.BeforeMethod != null || middleware.AfterMethod != null || middleware.FinallyMethod != null;
        }

        // Async handlers can work with both sync and async middleware
        return true;
    }

    private static void GenerateDirectHandlerExecution(IndentedStringBuilder source, HandlerInfo handler)
    {
        // Generate method call based on whether the handler method is static or instance
        string methodCall;
        if (handler.IsStatic)
        {
            // For static methods, call directly on the handler type
            methodCall = GenerateStaticMethodCall(handler, "message", "cancellationToken", "serviceProvider");
        }
        else
        {
            // For instance methods, get handler instance first
            source.AppendLine("var handlerInstance = GetOrCreateHandler(serviceProvider);");
            methodCall = GenerateMethodCall(handler, "handlerInstance", "message", "cancellationToken");
        }

        // Handle the return based on the original return type
        if (handler.ReturnTypeName == null)
        {
            // For void methods, call the method without returning anything
            source.AppendLine($"{methodCall};");
        }
        else
        {
            // For all other cases, just return the result directly
            source.AppendLine($"return {methodCall};");
        }
    }

    private static void GenerateMiddlewareExecutionCore(IndentedStringBuilder source, HandlerInfo handler, List<MiddlewareInfo> middlewares, bool isAsync)
    {
        // Generate middleware instances with descriptive names
        string[] middlewareVariableNames = new string[middlewares.Count];
        string[] resultVariableNames = new string[middlewares.Count];
        for (int i = 0; i < middlewares.Count; i++)
        {
            string variableName = GetMiddlewareVariableName(middlewares[i].MiddlewareTypeName);
            string resultVariableName = GetMiddlewareResultVariableName(middlewares[i].MiddlewareTypeName);
            middlewareVariableNames[i] = variableName;
            resultVariableNames[i] = resultVariableName;
            // Only create middleware instance if it's not static
            if (!middlewares[i].IsStatic)
            {
                source.AppendLine($"var {variableName} = global::Foundatio.Mediator.Mediator.GetOrCreateMiddleware<{middlewares[i].MiddlewareTypeName}>(serviceProvider);");
            }
        }

        source.AppendLine();

        // Generate individual result variables instead of an array
        for (int i = 0; i < middlewares.Count; i++)
        {
            string resultType = GetMiddlewareResultType(middlewares[i]);
            string defaultValue = GetDefaultValueForMiddleware(middlewares[i], resultType);
            source.AppendLine($"{resultType} {resultVariableNames[i]} = {defaultValue};");
        }

        source.AppendLine();

        // Only generate handlerResult if it's actually needed
        bool needsHandlerResult = NeedsHandlerResultVariable(handler, middlewares);
        if (needsHandlerResult)
        {
            source.AppendLine(GetHandlerResultDeclaration(handler));
        }

        source.AppendLine("Exception? exception = null;");
        source.AppendLine();
        source.AppendLine("try");
        source.AppendLine("{");

        using (source.Indent())
        {
            // Before middleware (in order)
            for (int i = 0; i < middlewares.Count; i++)
            {
                var middleware = middlewares[i];
                string resultVar = resultVariableNames[i];
                if (middleware.BeforeMethod != null)
                {
                    var methodInfo = middleware.BeforeMethod.Value;
                    string args = String.Join(", ", methodInfo.Parameters.Select(p =>
                        p.IsMessage ? "message" :
                        p.IsCancellationToken ? "cancellationToken" :
                        $"serviceProvider.GetRequiredService<{p.TypeName}>()"));
                    string beforeMethodCall = GenerateMiddlewareMethodCall(middleware, methodInfo, args, middlewareVariableNames[i]);
                    if (methodInfo.IsAsync)
                        source.AppendLine($"{resultVar} = await {beforeMethodCall};");
                    else if (methodInfo.ReturnTypeName != "void")
                        source.AppendLine($"{resultVar} = {beforeMethodCall};");
                    else
                        source.AppendLine($"{beforeMethodCall};");
                }

                // Only check for HandlerResult if the method can actually return one
                if (CanReturnHandlerResult(middleware.BeforeMethod))
                {
                    source.AppendLine(GenerateShortCircuitCheck(middleware, resultVar, $"hr{i}"));
                    source.AppendLine("{");
                    using (source.Indent())
                    {
                        if (handler.ReturnTypeName == null)
                        {
                            source.AppendLine("return;");
                        }
                        else
                        {
                            source.AppendLine($"return {GetHandlerResultValueExpression(middleware, resultVar, $"hr{i}", handler, useUnwrappedType: true)};");
                        }
                    }
                    source.AppendLine("}");
                }
            }

            source.AppendLine();

            // Handler execution
            string methodCall;
            if (handler.IsStatic)
            {
                methodCall = GenerateStaticMethodCall(handler, "message", "cancellationToken", "serviceProvider");
            }
            else
            {
                source.AppendLine("var handlerInstance = GetOrCreateHandler(serviceProvider);");
                methodCall = GenerateMethodCall(handler, "handlerInstance", "message", "cancellationToken");
            }

            if (handler.ReturnTypeName == null)
            {
                if (handler.IsAsync)
                {
                    source.AppendLine($"await {methodCall};");
                }
                else
                {
                    source.AppendLine($"{methodCall};");
                }
            }
            else
            {
                if (handler.IsAsync)
                {
                    source.AppendLine($"handlerResult = await {methodCall};");
                }
                else
                {
                    source.AppendLine($"handlerResult = {methodCall};");
                }
            }
            source.AppendLine();

            // After middleware (in order)
            for (int i = 0; i < middlewares.Count; i++)
            {
                var middleware = middlewares[i];
                string resultVar = resultVariableNames[i];
                if (middleware.AfterMethod != null)
                {
                    var methodInfo = middleware.AfterMethod.Value;
                    string args = String.Join(", ", methodInfo.Parameters.Select(p =>
                        GenerateMiddlewareParameterExpression(p, middleware, resultVar, handler)));
                    string afterMethodCall = GenerateMiddlewareMethodCall(middleware, methodInfo, args, middlewareVariableNames[i]);
                    if (methodInfo.IsAsync)
                        source.AppendLine($"await {afterMethodCall};");
                    else
                        source.AppendLine($"{afterMethodCall};");
                }
            }

            if (handler.ReturnTypeName != null)
            {
                source.AppendLine($"return ({handler.ReturnTypeName})handlerResult;");
            }
        }

        source.AppendLine("}");
        source.AppendLine("catch (Exception ex)");
        source.AppendLine("{");
        using (source.Indent())
        {
            source.AppendLine("exception = ex;");
            source.AppendLine("throw;");
        }
        source.AppendLine("}");
        source.AppendLine("finally");
        source.AppendLine("{");

        using (source.Indent())
        {
            // Finally middleware (in reverse order)
            for (int i = middlewares.Count - 1; i >= 0; i--)
            {
                var middleware = middlewares[i];
                string resultVar = resultVariableNames[i];
                if (middleware.FinallyMethod != null)
                {
                    var methodInfo = middleware.FinallyMethod.Value;
                    string args = String.Join(", ", methodInfo.Parameters.Select(p =>
                        GenerateMiddlewareParameterExpression(p, middleware, resultVar, handler)));
                    string finallyMethodCall = GenerateMiddlewareMethodCall(middleware, methodInfo, args, middlewareVariableNames[i]);
                    if (methodInfo.IsAsync)
                        source.AppendLine($"await {finallyMethodCall};");
                    else
                        source.AppendLine($"{finallyMethodCall};");
                }
            }
        }

        source.AppendLine("}");
    }

    /// <summary>
    /// Determines if the handlerResult variable is needed based on handler return type and middleware usage.
    /// </summary>
    private static bool NeedsHandlerResultVariable(HandlerInfo handler, List<MiddlewareInfo> middlewares)
    {
        // For void/Task handlers, never generate handlerResult variable - pass null to middleware instead
        if (handler.ReturnTypeName == null)
        {
            return false;
        }

        // For non-void handlers, we always need handlerResult for the return statement
        return true;
    }

    /// <summary>
    /// Gets the proper variable declaration for a handler result with nullable-safe initialization.
    /// </summary>
    private static string GetHandlerResultDeclaration(HandlerInfo handler)
    {
        if (handler.ReturnTypeIsNullable)
            return $"{handler.ReturnTypeName}? handlerResult = null;";

        string returnType = handler.ReturnTypeName ?? "object";
        string defaultValue = GetDefaultValueForHandler(handler, returnType);
        return $"{returnType} handlerResult = {defaultValue};";
    }

    /// <summary>
    /// Gets a nullable-safe cast expression for HandlerResult.Value.
    /// </summary>
    private static string GetSafeCastExpression(string handlerResultVar, HandlerInfo handler, bool useUnwrappedType = false)
    {
        string returnType = useUnwrappedType || !handler.HasReturnValue
            ? GetUnwrappedReturnType(handler)
            : handler.OriginalReturnTypeName ?? "object";

        // Special handling for Result to Result<T> conversion
        if (returnType.StartsWith("Foundatio.Mediator.Result<") && returnType != "Foundatio.Mediator.Result")
        {
            // Check if the value might be a non-generic Result that needs conversion to Result<T>
            return $"{handlerResultVar}.Value is Foundatio.Mediator.Result result ? ({returnType})result : ({returnType}?){handlerResultVar}.Value ?? default({returnType})!";
        }

        // For reference types, provide a null-coalescing fallback to satisfy non-nullable return types
        if (handler.ReturnTypeIsNullable || handler.ReturnTypeIsNullable)
        {
            string defaultValue = GetDefaultValueForHandler(handler, returnType);
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
                private static {{handler.HandlerTypeName}}? _handler;
                private static readonly object _lock = new object();

                [DebuggerStepThrough]
                private static {{handler.HandlerTypeName}} GetOrCreateHandler(IServiceProvider serviceProvider)
                {
                    if (_handler != null)
                        return _handler;

                    var handlerFromDI = serviceProvider.GetService<{{handler.HandlerTypeName}}>();
                    if (handlerFromDI != null)
                        return handlerFromDI;

                    lock (_lock)
                    {
                        if (_handler != null)
                            return _handler;

                        _handler = ActivatorUtilities.CreateInstance<{{handler.HandlerTypeName}}>(serviceProvider);
                        return _handler;
                    }
                }
                """);
    }

    private static string GenerateShortCircuitCheck(MiddlewareInfo middleware, string resultVariableName, string hrVariableName)
    {
        if (middleware.BeforeMethod == null)
            return $"if ({resultVariableName} is HandlerResult {hrVariableName} && {hrVariableName}.IsShortCircuited)";

        var methodInfo = middleware.BeforeMethod.Value;

        // Use the unwrapped return type for async methods, original for sync methods
        string returnType = methodInfo.IsAsync ? (methodInfo.ReturnTypeName ?? "object") : (methodInfo.OriginalReturnTypeName ?? "object");

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
            return $"if ({resultVariableName} is HandlerResult {hrVariableName} && {hrVariableName}.IsShortCircuited)";
        }
    }

    private static string GetHandlerResultValueExpression(MiddlewareInfo middleware, string resultVariableName, string hrVariableName, HandlerInfo handler, bool useUnwrappedType = false)
    {
        if (middleware.BeforeMethod == null)
            return GetSafeCastExpression(hrVariableName, handler, useUnwrappedType);

        var methodInfo = middleware.BeforeMethod.Value;

        // Use the unwrapped return type for async methods, original for sync methods
        string returnType = methodInfo.IsAsync ? (methodInfo.ReturnTypeName ?? "object") : (methodInfo.OriginalReturnTypeName ?? "object");

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
            return $"{middleware.MiddlewareTypeName}.{method.MethodName}({parameters})";
        }
        else
        {
            return $"{middlewareVariableName}.{method.MethodName}({parameters})";
        }
    }

    private static void GenerateOptimizedTupleHandling(IndentedStringBuilder source, HandlerInfo handler, string expectedResponseTypeName, bool isAsync)
    {
        var tupleFields = handler.ReturnTypeTupleItems.ToList();

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
            string fieldType = tupleItem.TypeName;

            if (IsTypeCompatible(fieldType, expectedResponseTypeName))
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
            string itemAccess = $"result.{GetTupleFieldAccessor(tupleItem)}";

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
            source.AppendLine($"return result.{GetTupleFieldAccessor(returnTupleItem)};");
        }
        else
        {
            source.AppendLine($"return default({expectedResponseTypeName})!;");
        }
    }

    private static bool IsTypeCompatible(string fieldType, string expectedType)
    {
        if (fieldType == expectedType)
            return true;

        string normalizedFieldType = NormalizeTypeName(fieldType);
        string normalizedExpectedType = NormalizeTypeName(expectedType);

        return normalizedFieldType == normalizedExpectedType;
    }

    private static string NormalizeTypeName(string typeName)
    {
        return typeName
            .Replace("System.", "")
            .Replace("global::", "")
            .Trim();
    }

    private static string GetMiddlewareVariableName(string middlewareTypeName)
    {
        string simpleName = Helpers.GetSimpleTypeName(middlewareTypeName);
        return $"middleware{simpleName}";
    }

    private static string GetMiddlewareResultVariableName(string middlewareTypeName)
    {
        string simpleName = Helpers.GetSimpleTypeName(middlewareTypeName);
        return $"result{simpleName}";
    }

    private static string GetMiddlewareResultType(MiddlewareInfo middleware)
    {
        if (middleware.BeforeMethod?.HasReturnValue == true)
        {
            var beforeMethod = middleware.BeforeMethod.Value;
            string returnType = beforeMethod.ReturnTypeName ?? "object?";

            if (!returnType.EndsWith("?") && IsReferenceTypeForNullability(returnType))
            {
                return $"{returnType}?";
            }

            return returnType;
        }
        return "object?";
    }

    private static bool CanReturnHandlerResult(MiddlewareMethodInfo? methodInfo)
    {
        if (methodInfo == null) return false;
        return methodInfo.Value.ReturnTypeIsHandlerResult;
    }

    private static string GenerateMiddlewareParameterExpression(ParameterInfo parameter, MiddlewareInfo middleware, string resultVariableName, HandlerInfo handler)
    {
        if (parameter.IsMessage)
            return "message";

        if (parameter.IsCancellationToken)
            return "cancellationToken";

        // Check if it's exception parameter
        if (parameter.TypeName == "Exception" || parameter.TypeName == "Exception?" ||
            parameter.TypeName == "System.Exception" || parameter.TypeName == "System.Exception?")
            return "exception";

        // Check if it's the result from this middleware's Before method
        if (middleware.BeforeMethod?.ReturnTypeName != null)
        {
            // If the Before method returns a simple (non-tuple) type, check direct match
            if (!middleware.BeforeMethod.Value.ReturnTypeIsTuple)
            {
                if (parameter.TypeName == middleware.BeforeMethod.Value.ReturnTypeName ||
                    parameter.TypeName == "object" || parameter.TypeName == "object?")
                    return resultVariableName;
            }
            else
            {
                // If the Before method returns a tuple, check if this parameter matches any tuple item
                var tupleItems = middleware.BeforeMethod.Value.ReturnTypeTupleItems.ToList();
                for (int i = 0; i < tupleItems.Count; i++)
                {
                    if (parameter.TypeName == tupleItems[i].TypeName)
                    {
                        return $"{resultVariableName}.{GetTupleFieldAccessor(tupleItems[i])}";
                    }
                }
            }
        }

        // Check if it's handler result parameter
        if (handler.HasReturnValue && (parameter.TypeName == handler.ReturnTypeName || parameter.TypeName == "object"))
            return "handlerResult";

        // Otherwise resolve from DI
        return $"serviceProvider.GetRequiredService<{parameter.TypeName}>()";
    }

    private static string GetUnwrappedReturnType(HandlerInfo handler)
    {
        if (handler.ReturnTypeName == null)
            return "object";

        return handler.ReturnTypeName;
    }

    private static string GetDefaultValueForType(bool isNullable, string typeName)
    {
        // For nullable types or object type, use null
        if (typeName.EndsWith("?") || isNullable || typeName == "object")
            return "null";

        return typeName switch
        {
            "string" => "string.Empty",
            "int" => "0",
            "bool" => "false",
            "long" => "0L",
            "double" => "0.0",
            "float" => "0.0f",
            "decimal" => "0m",
            // For non-nullable value types, use default
            _ => "default"
        };
    }

    private static string GetDefaultValueForHandler(HandlerInfo handler, string typeName)
    {
        return GetDefaultValueForType(handler.ReturnTypeIsNullable, typeName);
    }

    private static string GetDefaultValueForMiddleware(MiddlewareInfo middleware, string typeName)
    {
        bool isNullable = middleware.BeforeMethod?.ReturnTypeIsNullable ?? false;
        return GetDefaultValueForType(isNullable, typeName);
    }

    private static bool IsReferenceTypeForNullability(string typeName)
    {
        // Known value types that don't need nullable annotation
        if (typeName == "int" || typeName == "bool" || typeName == "long" ||
            typeName == "double" || typeName == "float" || typeName == "decimal" ||
            typeName == "DateTime" || typeName == "Guid" || typeName == "TimeSpan" ||
            typeName == "HandlerResult" || typeName == "Foundatio.Mediator.HandlerResult" ||
            typeName.StartsWith("(")) // Tuple types
        {
            return false;
        }

        // Everything else is considered a reference type that should be nullable
        return true;
    }

    private static string GetTupleFieldAccessor(TupleItemInfo tupleItem)
    {
        return !String.IsNullOrEmpty(tupleItem.Name) ? tupleItem.Name : tupleItem.FieldName;
    }
}
