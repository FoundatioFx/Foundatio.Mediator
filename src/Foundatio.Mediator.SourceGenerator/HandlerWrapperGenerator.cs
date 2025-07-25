using Microsoft.CodeAnalysis;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class HandlerWrapperGenerator
{
    public static void GenerateHandlerWrappers(List<HandlerInfo> handlers, List<MiddlewareInfo> middlewares, bool interceptorsEnabled, SourceProductionContext context)
    {
        foreach (var handler in handlers)
        {
            string wrapperClassName = GetWrapperClassName(handler);

            var applicableMiddlewares = GetApplicableMiddlewares(middlewares, handler);

            string source = GenerateHandlerWrapper(handler, wrapperClassName, applicableMiddlewares, interceptorsEnabled);
            string fileName = $"{wrapperClassName}.g.cs";
            context.AddSource(fileName, source);
        }
    }

    public static string GenerateHandlerWrapper(HandlerInfo handler, string wrapperClassName, List<MiddlewareInfo> middlewares, bool interceptorsEnabled)
    {
        var source = new IndentedStringBuilder();

        AddGeneratedFileHeader(source);

        source.AppendLine("#nullable enable")
              .AppendLine("using System;")
              .AppendLine("using System.Collections.Generic;")
              .AppendLine("using System.Linq;")
              .AppendLine("using System.Reflection;")
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

            // Generate single generic method based on effective async status
            if (needsAsyncHandleMethod)
            {
                GenerateAsyncHandleMethod(source, handler);
            }
            else
            {
                GenerateSyncHandleMethod(source, handler);
            }

            // Generate interceptor methods if interceptors are enabled and there are call sites
            var callSites = handler.CallSites.ToList();
            if (interceptorsEnabled && callSites.Count > 0)
            {
                GenerateInterceptorMethods(source, handler, callSites, middlewares);
            }

            // Only generate GetOrCreateHandler for instance methods (not static methods)
            if (!handler.IsStatic)
            {
                GenerateGetOrCreateHandler(source, handler);
            }

            // Add helper method for tuple handling if needed
            bool hasReturnValue = handler.ReturnTypeName != "void" &&
                                  handler.ReturnTypeName != "System.Threading.Tasks.Task" &&
                                  !String.IsNullOrEmpty(handler.ReturnTypeName);

            if (hasReturnValue && IsTupleReturnType(handler.ReturnTypeName))
            {
                GenerateHandleTupleResult(source);
            }
        }

        source.AppendLine("}");

        return source.ToString();
    }

    public static string GetWrapperClassName(HandlerInfo handler)
    {
        // Create a deterministic wrapper class name based on handler type and method
        // Extract the simple type name from the full type name, handling both . and + separators
        string handlerTypeName = TypeNameHelper.GetSimpleTypeName(handler.HandlerTypeName);
        string methodName = handler.MethodName;
        string messageTypeName = TypeNameHelper.GetSimpleTypeName(handler.MessageTypeName);
        return $"{handlerTypeName}_{methodName}_{messageTypeName}_StaticWrapper";
    }

    public static string GetStronglyTypedMethodName(HandlerInfo handler)
    {
        // Use consistent method name for the strongly typed handler
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

        source.AppendLine($"public static {asyncModifier}{returnType} {stronglyTypedMethodName}({handler.MessageTypeName} message, IServiceProvider serviceProvider, CancellationToken cancellationToken)")
              .AppendLine("{");

        using (source.Indent())
        {
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
                if (IsVoidReturnType(handler.OriginalReturnTypeName))
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

    private static bool IsVoidReturnType(string returnTypeName)
    {
        return returnTypeName == "void" || returnTypeName == "System.Threading.Tasks.Task";
    }

    /// <summary>
    /// Unwraps Task&lt;T&gt; and ValueTask&lt;T&gt; to get the inner type T.
    /// Returns the original type if it's not a task type.
    /// </summary>
    private static string UnwrapTaskType(string returnType)
    {
        // Handle System.Threading.Tasks.Task<T>
        if (returnType.StartsWith("System.Threading.Tasks.Task<") && returnType.EndsWith(">"))
        {
            return returnType.Substring("System.Threading.Tasks.Task<".Length, returnType.Length - "System.Threading.Tasks.Task<".Length - 1);
        }
        // Handle Task<T>
        else if (returnType.StartsWith("Task<") && returnType.EndsWith(">"))
        {
            return returnType.Substring(5, returnType.Length - 6); // Remove "Task<" and ">"
        }
        // Handle System.Threading.Tasks.ValueTask<T>
        else if (returnType.StartsWith("System.Threading.Tasks.ValueTask<") && returnType.EndsWith(">"))
        {
            return returnType.Substring("System.Threading.Tasks.ValueTask<".Length, returnType.Length - "System.Threading.Tasks.ValueTask<".Length - 1);
        }
        // Handle ValueTask<T>
        else if (returnType.StartsWith("ValueTask<") && returnType.EndsWith(">"))
        {
            return returnType.Substring(10, returnType.Length - 11); // Remove "ValueTask<" and ">"
        }

        return returnType;
    }

    private static string GetUnwrappedReturnType(HandlerInfo handler)
    {
        // If it's an async method returning Task<T>, we want just T
        if (handler.IsAsync)
        {
            return UnwrapTaskType(handler.OriginalReturnTypeName);
        }

        // For other types, return as-is
        return handler.OriginalReturnTypeName;
    }

    private static string ReconstructOriginalReturnType(HandlerInfo handler, bool hasAsyncMiddleware = false)
    {
        // Use the original return type directly from the handler method
        string originalReturnType = handler.OriginalReturnTypeName;

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

    private static bool IsTupleReturnType(string returnTypeName)
    {
        return returnTypeName.Contains("ValueTuple") || returnTypeName.Contains("Tuple") || returnTypeName.StartsWith("(");
    }

    private static bool IsReferenceType(string returnTypeName)
    {
        // Simple heuristic for common value types
        string[] valueTypes = new[] { "int", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort",
                                "float", "double", "decimal", "bool", "char", "DateTime", "TimeSpan", "DateTimeOffset", "Guid" };

        string typeName = returnTypeName.Replace("System.", "").Replace("?", "");
        return !valueTypes.Contains(typeName) && !typeName.Contains("Task<") || returnTypeName.Contains("?");
    }

    private static void GenerateAsyncHandleMethod(IndentedStringBuilder source, HandlerInfo handler)
    {
        source.AppendLine("public static async ValueTask<object?> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)")
              .AppendLine("{");

        using (source.Indent())
        {
            // Cast message to expected type and call strongly typed method
            source.AppendLine($"var typedMessage = ({handler.MessageTypeName})message;")
                  .AppendLine("var serviceProvider = ((Mediator)mediator).ServiceProvider;");

            bool hasReturnValue = handler.ReturnTypeName != "void" &&
                                  handler.ReturnTypeName != "System.Threading.Tasks.Task" &&
                                  !String.IsNullOrEmpty(handler.ReturnTypeName);

            string stronglyTypedMethodName = GetStronglyTypedMethodName(handler);

            if (hasReturnValue)
            {
                source.AppendLine($"var result = await {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");

                // Handle tuple return values and cascading
                if (IsTupleReturnType(handler.ReturnTypeName))
                {
                    source.AppendLine("return await HandleTupleResult(mediator, result, responseType, cancellationToken);");
                }
                else
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

                    // Only generate IResult handling if the handler returns a Result type
                    if (IsResultType(handler.ReturnTypeName) || IsResultType(handler.OriginalReturnTypeName))
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

                    source.AppendLine("return result;");
                }
            }
            else
            {
                // Handler returns void
                source.AppendLine($"await {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);")
                      .AppendLine("return new object();");
            }
        }

        source.AppendLine("}");
    }

    private static void GenerateSyncHandleMethod(IndentedStringBuilder source, HandlerInfo handler)
    {
        source.AppendLine("public static object? UntypedHandle(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)")
              .AppendLine("{");

        using (source.Indent())
        {
            // Cast message to expected type and call strongly typed method
            source.AppendLine($"var typedMessage = ({handler.MessageTypeName})message;")
                  .AppendLine("var serviceProvider = ((Mediator)mediator).ServiceProvider;");

            bool hasReturnValue = handler.ReturnTypeName != "void" &&
                                  handler.ReturnTypeName != "System.Threading.Tasks.Task" &&
                                  !String.IsNullOrEmpty(handler.ReturnTypeName);

            string stronglyTypedMethodName = GetStronglyTypedMethodName(handler);

            if (hasReturnValue)
            {
                source.AppendLine($"var result = {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");

                // Handle tuple return values and cascading
                if (IsTupleReturnType(handler.ReturnTypeName))
                {
                    source.AppendLine("// TODO: Handle tuple result synchronously - for now return the tuple as-is")
                          .AppendLine("return result;");
                }
                else
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


                    // Only generate IResult handling if the handler returns a Result type
                    if (IsResultType(handler.ReturnTypeName) || IsResultType(handler.OriginalReturnTypeName))
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

                    source.AppendLine("return result;");
                }
            }
            else
            {
                // Handler returns void
                source.AppendLine($"{stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);")
                      .AppendLine("return new object();");
            }
        }

        source.AppendLine("}");
    }

    private static void GenerateInterceptorMethods(IndentedStringBuilder source, HandlerInfo handler, List<CallSiteInfo> callSites, List<MiddlewareInfo> middlewares)
    {
        // Group call sites by method signature to generate unique interceptor methods
        var callSiteGroups = callSites
            .GroupBy(cs => new { cs.MethodName, cs.MessageTypeName, cs.ExpectedResponseTypeName })
            .ToList();

        int methodCounter = 0;
        foreach (var group in callSiteGroups)
        {
            var key = group.Key;
            var groupCallSites = group.ToList();

            GenerateInterceptorMethod(source, handler, key.MethodName, key.ExpectedResponseTypeName, groupCallSites, methodCounter++, middlewares);
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
            source.AppendLine($"var typedMessage = ({handler.MessageTypeName})message;")
                  .AppendLine("var serviceProvider = ((Mediator)mediator).ServiceProvider;");

            // Generate the appropriate method call based on async/sync combinations
            string awaitKeyword = (interceptorIsAsync && wrapperIsAsync) ? "await " : "";
            string returnKeyword = isGeneric ? "return " : "";

            source.AppendLine($"{returnKeyword}{awaitKeyword}{stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");
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
            return (middleware.BeforeMethod != null && !middleware.BeforeMethod.IsAsync) ||
                   (middleware.AfterMethod != null && !middleware.AfterMethod.IsAsync) ||
                   (middleware.FinallyMethod != null && !middleware.FinallyMethod.IsAsync) ||
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
        if (handler.OriginalReturnTypeName == "void")
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
            source.AppendLine($"{resultType} {resultVariableNames[i]} = default;");
        }

        source.AppendLine();
        source.AppendLine(GetHandlerResultDeclaration(handler));
        source.AppendLine(GetExceptionDeclaration());
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
                    var methodInfo = middleware.BeforeMethod;
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
                        if (IsVoidReturnType(handler.OriginalReturnTypeName))
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

            if (IsVoidReturnType(handler.OriginalReturnTypeName))
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

            // After middleware (in order)
            for (int i = 0; i < middlewares.Count; i++)
            {
                var middleware = middlewares[i];
                string resultVar = resultVariableNames[i];
                if (middleware.AfterMethod != null)
                {
                    var methodInfo = middleware.AfterMethod;
                    string args = String.Join(", ", methodInfo.Parameters.Select(p =>
                        GenerateMiddlewareParameterExpression(p, middleware, resultVar)));
                    string afterMethodCall = GenerateMiddlewareMethodCall(middleware, methodInfo, args, middlewareVariableNames[i]);
                    if (methodInfo.IsAsync)
                        source.AppendLine($"await {afterMethodCall};");
                    else
                        source.AppendLine($"{afterMethodCall};");
                }
            }

            if (IsVoidReturnType(handler.OriginalReturnTypeName))
            {
                // For void methods, don't return anything
            }
            else
            {
                source.AppendLine($"return ({GetUnwrappedReturnType(handler)})handlerResult;");
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
                    var methodInfo = middleware.FinallyMethod;
                    string args = String.Join(", ", methodInfo.Parameters.Select(p =>
                        GenerateMiddlewareParameterExpression(p, middleware, resultVar)));
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
    /// Gets the proper variable declaration for a handler result with nullable-safe initialization.
    /// </summary>
    private static string GetHandlerResultDeclaration(HandlerInfo handler)
    {
        // For void methods, we still need a placeholder variable for middleware compatibility
        if (IsVoidReturnType(handler.OriginalReturnTypeName))
        {
            return "#pragma warning disable CS0219 // Variable assigned but never used\n" +
                   "        object? handlerResult = null;\n" +
                   "        #pragma warning restore CS0219";
        }

        string returnType = GetUnwrappedReturnType(handler);

        // Check if it's a reference type that should be nullable
        if (IsReferenceType(returnType))
        {
            return $"{returnType}? handlerResult = null;";
        }

        return $"{returnType} handlerResult = default({returnType});";
    }

    /// <summary>
    /// Gets the proper Exception variable declaration with nullable annotation.
    /// </summary>
    private static string GetExceptionDeclaration()
    {
        return "Exception? exception = null;";
    }

    /// <summary>
    /// Gets a nullable-safe cast expression for HandlerResult.Value.
    /// </summary>
    private static string GetSafeCastExpression(string handlerResultVar, HandlerInfo handler, bool useUnwrappedType = false)
    {
        string returnType = useUnwrappedType || IsVoidReturnType(handler.OriginalReturnTypeName)
            ? GetUnwrappedReturnType(handler)
            : handler.OriginalReturnTypeName;

        // Special handling for Result to Result<T> conversion
        if (returnType.StartsWith("Foundatio.Mediator.Result<") && returnType != "Foundatio.Mediator.Result")
        {
            // Check if the value might be a non-generic Result that needs conversion to Result<T>
            return $"{handlerResultVar}.Value is Foundatio.Mediator.Result result ? ({returnType})result : ({returnType}?){handlerResultVar}.Value ?? default({returnType})!";
        }

        if (IsReferenceType(returnType))
        {
            // For reference types, provide a null-coalescing fallback to satisfy non-nullable return types
            string defaultValue = GetDefaultValueForType(returnType);
            return $"({returnType}?){handlerResultVar}.Value ?? {defaultValue}";
        }

        return $"({returnType}){handlerResultVar}.Value!";
    }

    /// <summary>
    /// Gets a default value expression for a given type.
    /// </summary>
    private static string GetDefaultValueForType(string typeName)
    {
        return typeName switch
        {
            "string" => "string.Empty",
            "object" => "new object()",
            _ when typeName.EndsWith("[]") => "new " + typeName + " { }",
            _ when typeName.StartsWith("List<") => "new " + typeName + "()",
            _ when typeName.StartsWith("IList<") => "new List<" + typeName.Substring(6),
            _ when typeName.StartsWith("IEnumerable<") => "new List<" + typeName.Substring(12),
            _ when IsReferenceType(typeName) => $"default({typeName})!",
            _ => $"default({typeName})"
        };
    }

    /// <summary>
    /// Generates a descriptive camelCase variable name from a middleware type name.
    /// E.g., "ConsoleSample.Middleware.GlobalMiddleware" -> "globalMiddleware"
    /// </summary>
    private static string GetMiddlewareVariableName(string middlewareTypeName)
    {
        return GetCamelCaseTypeName(middlewareTypeName);
    }

    private static string GetMiddlewareResultVariableName(string middlewareTypeName)
    {
        return GetCamelCaseTypeName(middlewareTypeName) + "Result";
    }

    /// <summary>
    /// Converts a fully qualified type name to camelCase.
    /// E.g., "ConsoleSample.Middleware.GlobalMiddleware" -> "globalMiddleware"
    /// </summary>
    private static string GetCamelCaseTypeName(string typeName)
    {
        // Extract the simple type name (last part after the last dot)
        string simpleTypeName = typeName.Contains('.')
            ? typeName.Substring(typeName.LastIndexOf('.') + 1)
            : typeName;

        // Convert to camelCase
        if (String.IsNullOrEmpty(simpleTypeName))
            return "middleware";

        // Convert first character to lowercase
        return Char.ToLowerInvariant(simpleTypeName[0]) + simpleTypeName.Substring(1);
    }

    /// <summary>
    /// Determines if a middleware method can return HandlerResult and should be checked for short-circuiting.
    /// </summary>
    private static bool CanReturnHandlerResult(MiddlewareMethodInfo? method)
    {
        if (method == null || method.ReturnTypeName == "void")
            return false;

        string returnType = method.ReturnTypeName;

        // Direct HandlerResult return
        if (returnType == "HandlerResult" || returnType == "Foundatio.Mediator.HandlerResult")
            return true;

        // Task<HandlerResult> or ValueTask<HandlerResult> for async methods
        if (method.IsAsync && (returnType.Contains("HandlerResult")))
            return true;

        // object or object? can potentially contain HandlerResult
        if (returnType == "object" || returnType == "object?")
            return true;

        return false;
    }

    /// <summary>
    /// Generates the appropriate parameter expression for middleware methods,
    /// including handling tuple field extraction from Before method results.
    /// </summary>
    private static string GenerateMiddlewareParameterExpression(ParameterInfo parameter, MiddlewareInfo middleware, string resultVariableName)
    {
        if (parameter.IsMessage)
            return "message";
        if (parameter.IsCancellationToken)
            return "cancellationToken";
        if (parameter.Name == "beforeResult")
            return resultVariableName;
        if (parameter.Name == "handlerResult")
            return "handlerResult";
        if (parameter.Name == "exception")
            return "exception";

        // Check if this parameter matches the Before method return type or a tuple field
        if (middleware.BeforeMethod != null)
        {
            string beforeReturnType = middleware.BeforeMethod.ReturnTypeName;

            // Direct type match
            if (parameter.TypeName == beforeReturnType)
            {
                return $"{resultVariableName}!";
            }

            // Handle nullable type matching - if parameter is nullable version of the before return type
            if (parameter.TypeName.EndsWith("?") && parameter.TypeName.TrimEnd('?') == beforeReturnType)
            {
                return $"{resultVariableName}";
            }

            // Handle reverse case - if before return type is nullable and parameter is non-nullable
            if (beforeReturnType.EndsWith("?") && beforeReturnType.TrimEnd('?') == parameter.TypeName)
            {
                return $"{resultVariableName}!";
            }

            // Tuple field extraction
            if (beforeReturnType.StartsWith("(") && beforeReturnType.EndsWith(")"))
            {
                var tupleFields = ParseTupleFields(beforeReturnType);
                for (int fieldIndex = 0; fieldIndex < tupleFields.Count; fieldIndex++)
                {
                    var field = tupleFields[fieldIndex];
                    if (field.Type == parameter.TypeName)
                    {
                        // Generate tuple field access using named field if available, otherwise use Item1, Item2, etc.
                        string fieldAccess = !string.IsNullOrEmpty(field.Name)
                            ? field.Name!
                            : $"Item{fieldIndex + 1}";
                        return $"{resultVariableName}!.Value.{fieldAccess}";
                    }
                }
            }
        }

        // Fall back to DI resolution
        return $"serviceProvider.GetRequiredService<{parameter.TypeName}>()";
    }

    /// <summary>
    /// Parses tuple type syntax to extract field types and optional names.
    /// E.g., "(DateTime Date, TimeSpan Time)" -> [("DateTime", "Date"), ("TimeSpan", "Time")]
    /// </summary>
    private static List<(string Type, string? Name)> ParseTupleFields(string tupleType)
    {
        var fields = new List<(string Type, string? Name)>();

        // Remove outer parentheses
        string content = tupleType.Trim('(', ')').Trim();

        // Split by comma, but be careful of nested generics
        var parts = new List<string>();
        string current = "";
        int depth = 0;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];
            if (c == ',' && depth == 0)
            {
                parts.Add(current.Trim());
                current = "";
            }
            else
            {
                if (c == '<' || c == '(') depth++;
                else if (c == '>' || c == ')') depth--;
                current += c;
            }
        }
        if (!String.IsNullOrEmpty(current.Trim()))
        {
            parts.Add(current.Trim());
        }

        // Parse each part: "TypeName FieldName" or just "TypeName"
        foreach (string? part in parts)
        {
            string[] tokens = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 1)
            {
                string type = tokens[0];
                string? name = tokens.Length >= 2 ? tokens[1] : null;
                fields.Add((type, name));
            }
        }

        return fields;
    }

    private static void AddGeneratedFileHeader(IndentedStringBuilder source)
    {
        source.AppendLine("// <auto-generated>");
        source.AppendLine("// This file was generated by Foundatio.Mediator source generators.");
        source.AppendLine("// Changes to this file may be lost when the code is regenerated.");
        source.AppendLine("// </auto-generated>");
        source.AppendLine();
        source.AppendLine("using System.Diagnostics;");
        source.AppendLine("using System.Diagnostics.CodeAnalysis;");
        source.AppendLine();
    }

    private static void GenerateHandleTupleResult(IndentedStringBuilder source)
    {
        source.AppendLine()
              .AppendLine("private static async ValueTask<object?> HandleTupleResult(IMediator mediator, object tupleResult, Type? responseType, CancellationToken cancellationToken)")
              .AppendLine("{");

        using (source.Indent())
        {
            source.AppendLine("// TODO: Implement tuple extraction and cascading message publishing")
                  .AppendLine("// For now, return the tuple as-is")
                  .AppendLine("return tupleResult;");
        }

        source.AppendLine("}");
    }

    private static void GenerateGetOrCreateHandler(IndentedStringBuilder source, HandlerInfo handler)
    {
        source.AppendLine()
              .AppendLine($"private static {handler.HandlerTypeName}? _handler;")
              .AppendLine("private static readonly object _lock = new object();")
              .AppendLine()
              .AppendLine("[DebuggerStepThrough]")
              .AppendLine($"private static {handler.HandlerTypeName} GetOrCreateHandler(IServiceProvider serviceProvider)")
              .AppendLine("{");

        using (source.Indent())
        {
            source.AppendLine("if (_handler != null)")
                  .AppendLine("    return _handler;")
                  .AppendLine()
                  .AppendLine($"var handlerFromDI = serviceProvider.GetService<{handler.HandlerTypeName}>();")
                  .AppendLine("if (handlerFromDI != null)")
                  .AppendLine("    return handlerFromDI;")
                  .AppendLine()
                  .AppendLine("lock (_lock)")
                  .AppendLine("{");

            using (source.Indent())
            {
                source.AppendLine("if (_handler != null)")
                      .AppendLine("    return _handler;")
                      .AppendLine()
                      .AppendLine($"_handler = ActivatorUtilities.CreateInstance<{handler.HandlerTypeName}>(serviceProvider);")
                      .AppendLine("return _handler;");
            }

            source.AppendLine("}");
        }

        source.AppendLine("}");
    }

    private static string GetMiddlewareResultType(MiddlewareInfo middleware)
    {
        if (middleware.BeforeMethod == null)
            return "object?";

        string returnType = middleware.BeforeMethod.ReturnTypeName;

        // Handle async methods - for the variable declaration, we want the unwrapped type
        // since we'll await the call and store the result
        if (middleware.BeforeMethod.IsAsync)
        {
            returnType = UnwrapTaskType(returnType);

            // Handle bare Task (no return value)
            if (returnType == "System.Threading.Tasks.Task" || returnType == "Task")
            {
                return "object?";
            }
        }

        // If the method returns void, we don't need a result variable
        if (returnType == "void")
            return "object?";

        // Make the type nullable if it isn't already, but NOT for HandlerResult types
        // HandlerResult should remain non-nullable since it's a struct-like type
        if (!returnType.EndsWith("?") && !returnType.Contains("HandlerResult") && returnType != "void")
            returnType += "?";

        return returnType;
    }

    private static string GenerateShortCircuitCheck(MiddlewareInfo middleware, string resultVariableName, string hrVariableName)
    {
        if (middleware.BeforeMethod == null)
            return $"if ({resultVariableName} is HandlerResult {hrVariableName} && {hrVariableName}.IsShortCircuited)";

        string returnType = middleware.BeforeMethod.ReturnTypeName;

        // Handle async methods - strip Task<T> wrapper
        if (middleware.BeforeMethod.IsAsync)
        {
            returnType = UnwrapTaskType(returnType);
        }

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

        string returnType = middleware.BeforeMethod.ReturnTypeName;

        // Handle async methods - strip Task<T> wrapper
        if (middleware.BeforeMethod.IsAsync)
        {
            returnType = UnwrapTaskType(returnType);
        }

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

    /// <summary>
    /// Generates the appropriate method call for middleware (static or instance-based).
    /// </summary>
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

    private static bool IsResultType(string returnTypeName)
    {
        // Unwrap any task types to get the core type
        string coreType = UnwrapTaskType(returnTypeName);

        return coreType == "Result" ||
               coreType == "Foundatio.Mediator.Result" ||
               coreType.StartsWith("Result<") ||
               coreType.StartsWith("Foundatio.Mediator.Result<");
    }
}
