using Microsoft.CodeAnalysis;
using System.Text;

namespace Foundatio.Mediator;

internal static class HandlerWrapperGenerator
{
    public static void GenerateHandlerWrappers(List<HandlerInfo> handlers, List<MiddlewareInfo> middlewares, List<CallSiteInfo> callSites, bool interceptorsEnabled, SourceProductionContext context)
    {
        // Group call sites by message type for easier lookup
        var callSitesByMessage = callSites
            .Where(cs => !cs.MethodName.StartsWith("Publish")) // Only process Invoke calls for interceptors
            .GroupBy(cs => cs.MessageTypeName)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var handler in handlers)
        {
            var wrapperClassName = GetWrapperClassName(handler);

            callSitesByMessage.TryGetValue(handler.MessageTypeName, out var handlerCallSites);
            handlerCallSites ??= new List<CallSiteInfo>();

            var source = GenerateStaticHandlerWrapper(handler, wrapperClassName, middlewares, handlerCallSites, interceptorsEnabled);
            var fileName = $"{wrapperClassName}.g.cs";
            context.AddSource(fileName, source);
        }
    }

    public static string GenerateStaticHandlerWrapper(HandlerInfo handler, string wrapperClassName, List<MiddlewareInfo> middlewares, List<CallSiteInfo> callSites, bool interceptorsEnabled)
    {
        var source = new StringBuilder();

        source.AppendLine("#nullable enable");
        source.AppendLine("using System;");
        source.AppendLine("using System.Threading;");
        source.AppendLine("using System.Threading.Tasks;");
        source.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        source.AppendLine();

        source.AppendLine("namespace Foundatio.Mediator");
        source.AppendLine("{");
        source.AppendLine($"    internal static class {wrapperClassName}");
        source.AppendLine("    {");

        // Generate strongly typed method that matches handler signature
        GenerateStronglyTypedMethod(source, handler, middlewares);

        // Determine if we need async handle method based on handler or middleware
        var applicableMiddlewares = GetApplicableMiddlewares(middlewares, handler);
        var hasAsyncMiddleware = applicableMiddlewares.Any(m => m.IsAsync);

        var needsAsyncHandleMethod = handler.IsAsync || hasAsyncMiddleware;

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
        if (interceptorsEnabled && callSites.Count > 0)
        {
            GenerateInterceptorMethods(source, handler, callSites, middlewares);
        }

        // Only generate GetOrCreateHandler for instance methods (not static methods)
        if (!handler.IsStatic)
        {
            source.AppendLine();
            source.AppendLine($"        private static {handler.HandlerTypeName}? _handler;");
            source.AppendLine("        private static readonly object _lock = new object();");
            source.AppendLine();
            source.AppendLine($"        private static {handler.HandlerTypeName} GetOrCreateHandler(IServiceProvider serviceProvider)");
            source.AppendLine("        {");
            source.AppendLine("            if (_handler != null)");
            source.AppendLine("                return _handler;");
            source.AppendLine();
            source.AppendLine($"            var handlerFromDI = serviceProvider.GetService<{handler.HandlerTypeName}>();");
            source.AppendLine("            if (handlerFromDI != null)");
            source.AppendLine("                return handlerFromDI;");
            source.AppendLine();
            source.AppendLine("            lock (_lock)");
            source.AppendLine("            {");
            source.AppendLine("                if (_handler != null)");
            source.AppendLine("                    return _handler;");
            source.AppendLine();
            source.AppendLine($"                _handler = ActivatorUtilities.CreateInstance<{handler.HandlerTypeName}>(serviceProvider);");
            source.AppendLine("                return _handler;");
            source.AppendLine("            }");
            source.AppendLine("        }");
        }

        // Generate GetOrCreateMiddleware methods for any middleware found
        if (middlewares.Any())
        {
            GenerateGetOrCreateMiddlewareMethod(source, middlewares);
        }

        // Add helper method for tuple handling if needed
        var hasReturnValue = handler.ReturnTypeName != "void" &&
                           handler.ReturnTypeName != "System.Threading.Tasks.Task" &&
                           !string.IsNullOrEmpty(handler.ReturnTypeName);

        if (hasReturnValue && IsTupleReturnType(handler.ReturnTypeName))
        {
            source.AppendLine();
            source.AppendLine("        private static async ValueTask<object> HandleTupleResult(IMediator mediator, object tupleResult, Type? responseType, CancellationToken cancellationToken)");
            source.AppendLine("        {");
            source.AppendLine("            // TODO: Implement tuple extraction and cascading message publishing");
            source.AppendLine("            // For now, return the tuple as-is");
            source.AppendLine("            return tupleResult;");
            source.AppendLine("        }");
        }

        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }

    public static string GetWrapperClassName(HandlerInfo handler)
    {
        // Create a deterministic wrapper class name based on handler type and method
        var handlerTypeName = handler.HandlerTypeName.Split('.').Last().Replace("<", "_").Replace(">", "_").Replace(",", "_");
        var methodName = handler.MethodName;
        var messageTypeName = handler.MessageTypeName.Split('.').Last().Replace("<", "_").Replace(">", "_").Replace(",", "_");
        return $"{handlerTypeName}_{methodName}_{messageTypeName}_StaticWrapper";
    }

    public static string GetStronglyTypedMethodName(HandlerInfo handler)
    {
        // Use consistent method name for the strongly typed handler
        return handler.IsAsync ? "HandleAsync" : "Handle";
    }

    private static void GenerateStronglyTypedMethod(StringBuilder source, HandlerInfo handler, List<MiddlewareInfo> middlewares)
    {
        var stronglyTypedMethodName = GetStronglyTypedMethodName(handler);

        // Get applicable middlewares for this handler
        var applicableMiddlewares = GetApplicableMiddlewares(middlewares, handler);

        // For the strongly typed method, we need to preserve the original method signature
        // but make it async if we have async middleware or the handler is async
        var hasAsyncMiddleware = applicableMiddlewares.Any(m => m.IsAsync);


        var returnType = ReconstructOriginalReturnType(handler, hasAsyncMiddleware);
        var isAsync = handler.IsAsync || hasAsyncMiddleware;

        var asyncModifier = isAsync ? "async " : "";

        source.AppendLine($"        public static {asyncModifier}{returnType} {stronglyTypedMethodName}({handler.MessageTypeName} message, IServiceProvider serviceProvider, CancellationToken cancellationToken)");
        source.AppendLine("        {");

        if (applicableMiddlewares.Any())
        {
            // Generate middleware-aware execution
            GenerateMiddlewareAwareExecution(source, handler, applicableMiddlewares, stronglyTypedMethodName);
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
                source.AppendLine($"            var handlerInstance = GetOrCreateHandler(serviceProvider);");
                methodCall = GenerateMethodCall(handler, "handlerInstance", "message", "cancellationToken");
            }

            // Handle the return based on the original return type
            if (IsVoidReturnType(handler.OriginalReturnTypeName))
            {
                // For void methods, call the method without returning anything
                if (handler.IsAsync)
                {
                    source.AppendLine($"            await {methodCall};");
                }
                else
                {
                    source.AppendLine($"            {methodCall};");
                }
            }
            else
            {
                // For all other cases, just return the result directly
                if (handler.IsAsync)
                {
                    source.AppendLine($"            return await {methodCall};");
                }
                else
                {
                    source.AppendLine($"            return {methodCall};");
                }
            }
        }

        source.AppendLine("        }");
        source.AppendLine();
    }

    private static bool IsVoidReturnType(string returnTypeName)
    {
        return returnTypeName == "void" || returnTypeName == "System.Threading.Tasks.Task";
    }

    private static string GetUnwrappedReturnType(HandlerInfo handler)
    {
        // If it's an async method returning Task<T>, we want just T
        if (handler.IsAsync && handler.OriginalReturnTypeName.StartsWith("System.Threading.Tasks.Task<"))
        {
            var innerType = handler.OriginalReturnTypeName.Substring("System.Threading.Tasks.Task<".Length);
            return innerType.Substring(0, innerType.Length - 1); // Remove the closing >
        }

        // For other types, return as-is
        return handler.OriginalReturnTypeName;
    }

    private static string ReconstructOriginalReturnType(HandlerInfo handler, bool hasAsyncMiddleware = false)
    {
        // Use the original return type directly from the handler method
        var originalReturnType = handler.OriginalReturnTypeName;

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

    public static string GetStaticMethodName(HandlerInfo handler)
    {
        // Use consistent method name for the static handler
        return "HandleAsync";
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

        var parameterList = string.Join(", ", parameters);
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

        var parameterList = string.Join(", ", parameters);
        return $"{handler.HandlerTypeName}.{handler.MethodName}({parameterList})";
    }

    private static bool IsTupleReturnType(string returnTypeName)
    {
        return returnTypeName.Contains("ValueTuple") || returnTypeName.Contains("Tuple") || returnTypeName.StartsWith("(");
    }

    private static bool IsReferenceType(string returnTypeName)
    {
        // Simple heuristic for common value types
        var valueTypes = new[] { "int", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort",
                                "float", "double", "decimal", "bool", "char", "DateTime", "TimeSpan", "DateTimeOffset", "Guid" };

        var typeName = returnTypeName.Replace("System.", "").Replace("?", "");
        return !valueTypes.Contains(typeName) && !typeName.Contains("Task<") || returnTypeName.Contains("?");
    }

    public static string GetSyncMethodName(HandlerInfo handler)
    {
        return $"HandleSyncGeneric_{handler.MessageTypeName.Replace(".", "_").Replace("+", "_")}";
    }

    private static void GenerateAsyncHandleMethod(StringBuilder source, HandlerInfo handler)
    {
        source.AppendLine("        public static async ValueTask<object> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)");
        source.AppendLine("        {");

        // Cast message to expected type and call strongly typed method
        source.AppendLine($"            var typedMessage = ({handler.MessageTypeName})message;");
        source.AppendLine("            var serviceProvider = ((Mediator)mediator).ServiceProvider;");

        var hasReturnValue = handler.ReturnTypeName != "void" &&
                           handler.ReturnTypeName != "System.Threading.Tasks.Task" &&
                           !string.IsNullOrEmpty(handler.ReturnTypeName);

        var stronglyTypedMethodName = GetStronglyTypedMethodName(handler);

        if (hasReturnValue)
        {
            source.AppendLine($"            var result = await {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");

            // Handle tuple return values and cascading
            if (IsTupleReturnType(handler.ReturnTypeName))
            {
                source.AppendLine("            return await HandleTupleResult(mediator, result, responseType, cancellationToken);");
            }
            else
            {
                // For reference types, handle null. For value types, just box directly
                if (IsReferenceType(handler.ReturnTypeName))
                {
                    source.AppendLine("            return result ?? new object();");
                }
                else
                {
                    source.AppendLine("            return result;");
                }
            }
        }
        else
        {
            // Handler returns void
            source.AppendLine($"            await {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");
            source.AppendLine("            return new object();");
        }

        source.AppendLine("        }");
    }

    private static void GenerateSyncHandleMethod(StringBuilder source, HandlerInfo handler)
    {
        source.AppendLine("        public static object UntypedHandle(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)");
        source.AppendLine("        {");

        // Cast message to expected type and call strongly typed method
        source.AppendLine($"            var typedMessage = ({handler.MessageTypeName})message;");
        source.AppendLine("            var serviceProvider = ((Mediator)mediator).ServiceProvider;");

        var hasReturnValue = handler.ReturnTypeName != "void" &&
                           handler.ReturnTypeName != "System.Threading.Tasks.Task" &&
                           !string.IsNullOrEmpty(handler.ReturnTypeName);

        var stronglyTypedMethodName = GetStronglyTypedMethodName(handler);

        if (hasReturnValue)
        {
            source.AppendLine($"            var result = {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");

            // Handle tuple return values and cascading
            if (IsTupleReturnType(handler.ReturnTypeName))
            {
                source.AppendLine("            // TODO: Handle tuple result synchronously - for now return the tuple as-is");
                source.AppendLine("            return result;");
            }
            else
            {
                // For reference types, handle null. For value types, just box directly
                if (IsReferenceType(handler.ReturnTypeName))
                {
                    source.AppendLine("            return result ?? new object();");
                }
                else
                {
                    source.AppendLine("            return result;");
                }
            }
        }
        else
        {
            // Handler returns void
            source.AppendLine($"            {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");
            source.AppendLine("            return new object();");
        }

        source.AppendLine("        }");
    }

    private static void GenerateInterceptorMethods(StringBuilder source, HandlerInfo handler, List<CallSiteInfo> callSites, List<MiddlewareInfo> middlewares)
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

            GenerateInterceptorMethod(source, handler, key.MethodName, key.MessageTypeName, key.ExpectedResponseTypeName, groupCallSites, methodCounter++, middlewares);
        }
    }

    private static void GenerateInterceptorMethod(StringBuilder source, HandlerInfo handler, string methodName, string messageTypeName, string expectedResponseTypeName, List<CallSiteInfo> callSites, int methodIndex, List<MiddlewareInfo> middlewares)
    {
        // Generate unique method name for the interceptor
        var interceptorMethodName = $"Intercept{methodName}{methodIndex}";

        // Determine if the wrapper method is async (either because the handler is async OR because there are async middleware)
        var applicableMiddlewares = GetApplicableMiddlewares(middlewares, handler);
        var hasAsyncMiddleware = applicableMiddlewares.Any(m =>
            (m.BeforeMethod?.IsAsync == true) ||
            (m.AfterMethod?.IsAsync == true) ||
            (m.FinallyMethod?.IsAsync == true));

        var wrapperIsAsync = handler.IsAsync || hasAsyncMiddleware;

        // The interceptor should match the original call signature (Invoke vs InvokeAsync)
        var interceptorIsAsync = methodName.EndsWith("Async");
        var isGeneric = !string.IsNullOrEmpty(expectedResponseTypeName);

        // Check for sync interceptor with async wrapper - this should generate a diagnostic error instead
        if (!interceptorIsAsync && wrapperIsAsync)
        {
            // Don't generate interceptor for sync calls with async middleware
            // This will be caught by the validator and generate FMED012 diagnostic
            return;
        }

        // Generate interceptor attributes for all call sites
        var interceptorAttributes = callSites
            .Select(cs => GenerateInterceptorAttribute(cs))
            .Where(attr => !string.IsNullOrEmpty(attr))
            .ToList();

        if (interceptorAttributes.Count == 0)
            return;

        source.AppendLine();

        // Add interceptor attributes
        foreach (var attribute in interceptorAttributes)
        {
            source.AppendLine($"        {attribute}");
        }

        // Generate method signature
        var returnType = GenerateInterceptorReturnType(interceptorIsAsync, isGeneric, expectedResponseTypeName);
        var parameters = "this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default";

        var stronglyTypedMethodName = GetStronglyTypedMethodName(handler);

        // The wrapper method is async if either the handler is async OR there are async middleware
        // var wrapperIsAsync = wrapperIsAsync; // Already defined above

        // Generate method signature based on whether the interceptor should be async
        if (interceptorIsAsync)
        {
            source.AppendLine($"        public static async {returnType} {interceptorMethodName}({parameters})");
            source.AppendLine("        {");
            source.AppendLine($"            var typedMessage = ({handler.MessageTypeName})message;");
            source.AppendLine($"            var serviceProvider = ((Mediator)mediator).ServiceProvider;");

            if (wrapperIsAsync)
            {
                // Both interceptor and wrapper are async
                if (isGeneric)
                {
                    source.AppendLine($"            return await {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");
                }
                else
                {
                    source.AppendLine($"            await {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");
                }
            }
            else
            {
                // Interceptor is async but wrapper is sync (shouldn't happen with current logic)
                if (isGeneric)
                {
                    source.AppendLine($"            return {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");
                }
                else
                {
                    source.AppendLine($"            {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");
                }
            }
        }
        else
        {
            // For sync interceptors, generate sync method signature
            source.AppendLine($"        public static {returnType} {interceptorMethodName}({parameters})");
            source.AppendLine("        {");
            source.AppendLine($"            var typedMessage = ({handler.MessageTypeName})message;");
            source.AppendLine($"            var serviceProvider = ((Mediator)mediator).ServiceProvider;");

            if (wrapperIsAsync)
            {
                // Interceptor is sync but wrapper is async - need to wait for result
                if (isGeneric)
                {
                    source.AppendLine($"            return await {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");
                }
                else
                {
                    source.AppendLine($"            await {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");
                }
            }
            else
            {
                // Both interceptor and wrapper are sync
                if (isGeneric)
                {
                    source.AppendLine($"            return {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");
                }
                else
                {
                    source.AppendLine($"            {stronglyTypedMethodName}(typedMessage, serviceProvider, cancellationToken);");
                }
            }
        }
        source.AppendLine("        }");
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

    private static void GenerateMiddlewareAwareExecution(StringBuilder source, HandlerInfo handler, List<MiddlewareInfo> applicableMiddlewares, string methodName)
    {
        // Determine if we need async execution based on handler or any async middleware
        var hasAsyncMiddleware = applicableMiddlewares.Any(m => m.IsAsync);
        var needsAsync = handler.IsAsync || hasAsyncMiddleware;

        if (applicableMiddlewares.Count == 1)
        {
            // Single middleware case
            var middleware = applicableMiddlewares[0];

            // Check compatibility
            if (!IsMiddlewareCompatibleWithHandler(middleware, handler))
            {
                // Generate error or fallback to direct handler execution
                GenerateDirectHandlerExecution(source, handler);
                return;
            }

            if (needsAsync)
            {
                GenerateAsyncSingleMiddlewareExecution(source, handler, middleware);
            }
            else
            {
                GenerateSyncSingleMiddlewareExecution(source, handler, middleware);
            }
        }
        else
        {
            // Multiple middleware case
            if (needsAsync)
            {
                GenerateAsyncMultipleMiddlewareExecution(source, handler, applicableMiddlewares);
            }
            else
            {
                GenerateSyncMultipleMiddlewareExecution(source, handler, applicableMiddlewares);
            }
        }
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

    private static void GenerateDirectHandlerExecution(StringBuilder source, HandlerInfo handler)
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
            source.AppendLine($"            var handlerInstance = GetOrCreateHandler(serviceProvider);");
            methodCall = GenerateMethodCall(handler, "handlerInstance", "message", "cancellationToken");
        }

        // Handle the return based on the original return type
        if (handler.OriginalReturnTypeName == "void")
        {
            // For void methods, call the method without returning anything
            source.AppendLine($"            {methodCall};");
        }
        else
        {
            // For all other cases, just return the result directly
            source.AppendLine($"            return {methodCall};");
        }
    }

    private static void GenerateAsyncSingleMiddlewareExecution(StringBuilder source, HandlerInfo handler, MiddlewareInfo middleware)
    {
        source.AppendLine($"            var middlewareInstance = GetOrCreateMiddleware<{middleware.MiddlewareTypeName}>(serviceProvider);");
        source.AppendLine("            object? beforeResult = null;");
        source.AppendLine(GetHandlerResultDeclaration(handler));
        source.AppendLine(GetExceptionDeclaration());
        source.AppendLine("            try");
        source.AppendLine("            {");

        // Before middleware
        if (middleware.BeforeMethod != null)
        {
            var beforeParams = string.Join(", ", middleware.BeforeMethod.Parameters.Select(p =>
                p.IsMessage ? "message" :
                p.IsCancellationToken ? "cancellationToken" :
                $"serviceProvider.GetRequiredService<{p.TypeName}>()"));
            if (middleware.BeforeMethod.IsAsync)
                source.AppendLine($"                beforeResult = await middlewareInstance.{middleware.BeforeMethod.MethodName}({beforeParams});");
            else if (middleware.BeforeMethod.ReturnTypeName != "void")
                source.AppendLine($"                beforeResult = middlewareInstance.{middleware.BeforeMethod.MethodName}({beforeParams});");
            else
                source.AppendLine($"                middlewareInstance.{middleware.BeforeMethod.MethodName}({beforeParams});");
        }

        // Only check for HandlerResult if the method can actually return one
        if (CanReturnHandlerResult(middleware.BeforeMethod))
        {
            source.AppendLine("                if (beforeResult is HandlerResult hr && hr.IsShortCircuited)");
            source.AppendLine("                {");
            if (IsVoidReturnType(handler.OriginalReturnTypeName))
            {
                source.AppendLine("                    return;");
            }
            else
            {
                // For async wrapper methods, return the unwrapped value directly
                source.AppendLine($"                    return {GetSafeCastExpression("hr", handler, useUnwrappedType: true)};");
            }
            source.AppendLine("                }");
        }

        // Handler execution
        string methodCall;
        if (handler.IsStatic)
        {
            methodCall = GenerateStaticMethodCall(handler, "message", "cancellationToken", "serviceProvider");
        }
        else
        {
            source.AppendLine("                var handlerInstance = GetOrCreateHandler(serviceProvider);");
            methodCall = GenerateMethodCall(handler, "handlerInstance", "message", "cancellationToken");
        }

        if (IsVoidReturnType(handler.OriginalReturnTypeName))
        {
            if (handler.IsAsync)
            {
                source.AppendLine($"                await {methodCall};");
            }
            else
            {
                source.AppendLine($"                {methodCall};");
            }
        }
        else
        {
            if (handler.IsAsync)
            {
                source.AppendLine($"                handlerResult = await {methodCall};");
            }
            else
            {
                source.AppendLine($"                handlerResult = {methodCall};");
            }
        }

        // After middleware
        if (middleware.AfterMethod != null && middleware.AfterMethod.IsAsync)
        {
            var methodInfo = middleware.AfterMethod;
            var args = string.Join(", ", methodInfo.Parameters.Select(p =>
                GenerateSingleMiddlewareParameterExpression(p, middleware)));
            source.AppendLine($"                await middlewareInstance.{methodInfo.MethodName}({args});");
        }
        else if (middleware.AfterMethod != null && !middleware.AfterMethod.IsAsync)
        {
            var methodInfo = middleware.AfterMethod;
            var args = string.Join(", ", methodInfo.Parameters.Select(p =>
                GenerateSingleMiddlewareParameterExpression(p, middleware)));
            source.AppendLine($"                middlewareInstance.{methodInfo.MethodName}({args});");
        }

        if (IsVoidReturnType(handler.OriginalReturnTypeName))
        {
            // For void methods, don't return anything
        }
        else
        {
            source.AppendLine("                return handlerResult;");
        }

        source.AppendLine("            }");
        source.AppendLine("            catch (Exception ex)");
        source.AppendLine("            {");
        source.AppendLine("                exception = ex;");
        source.AppendLine("                throw;");
        source.AppendLine("            }");
        source.AppendLine("            finally");
        source.AppendLine("            {");

        // Finally middleware
        if (middleware.FinallyMethod != null)
        {
            var finallyParams = string.Join(", ", middleware.FinallyMethod.Parameters.Select(p =>
                GenerateSingleMiddlewareParameterExpression(p, middleware)));
            if (middleware.FinallyMethod.IsAsync)
                source.AppendLine($"                await middlewareInstance.{middleware.FinallyMethod.MethodName}({finallyParams});");
            else
                source.AppendLine($"                middlewareInstance.{middleware.FinallyMethod.MethodName}({finallyParams});");
        }

        source.AppendLine("            }");
    }

    private static void GenerateSyncSingleMiddlewareExecution(StringBuilder source, HandlerInfo handler, MiddlewareInfo middleware)
    {
        source.AppendLine($"            var middlewareInstance = GetOrCreateMiddleware<{middleware.MiddlewareTypeName}>(serviceProvider);");
        source.AppendLine("            object? beforeResult = null;");
        source.AppendLine(GetHandlerResultDeclaration(handler));
        source.AppendLine(GetExceptionDeclaration());
        source.AppendLine("            try");
        source.AppendLine("            {");

        // Before middleware
        if (middleware.BeforeMethod != null)
        {
            var beforeParams = string.Join(", ", middleware.BeforeMethod.Parameters.Select(p =>
                p.IsMessage ? "message" :
                p.IsCancellationToken ? "cancellationToken" :
                $"serviceProvider.GetRequiredService<{p.TypeName}>()"));
            if (!middleware.BeforeMethod.IsAsync)
                source.AppendLine($"                beforeResult = middlewareInstance.{middleware.BeforeMethod.MethodName}({beforeParams});");
            else
                source.AppendLine($"                beforeResult = await middlewareInstance.{middleware.BeforeMethod.MethodName}({beforeParams});");
        }

        // Only check for HandlerResult if the method can actually return one
        if (CanReturnHandlerResult(middleware.BeforeMethod))
        {
            source.AppendLine("                if (beforeResult is HandlerResult hr && hr.IsShortCircuited)");
            source.AppendLine("                {");
            if (IsVoidReturnType(handler.OriginalReturnTypeName))
            {
                source.AppendLine("                    return;");
            }
            else
            {
                // For sync handlers, return the value directly
                source.AppendLine($"                    return {GetSafeCastExpression("hr", handler)};");
            }
            source.AppendLine("                }");
        }

        // Handler execution
        string methodCall;
        if (handler.IsStatic)
        {
            methodCall = GenerateStaticMethodCall(handler, "message", "cancellationToken", "serviceProvider");
        }
        else
        {
            source.AppendLine("                var handlerInstance = GetOrCreateHandler(serviceProvider);");
            methodCall = GenerateMethodCall(handler, "handlerInstance", "message", "cancellationToken");
        }

        if (IsVoidReturnType(handler.OriginalReturnTypeName))
        {
            if (handler.IsAsync)
            {
                source.AppendLine($"                await {methodCall};");
            }
            else
            {
                source.AppendLine($"                {methodCall};");
            }
        }
        else
        {
            if (handler.IsAsync)
            {
                source.AppendLine($"                handlerResult = await {methodCall};");
            }
            else
            {
                source.AppendLine($"                handlerResult = {methodCall};");
            }
        }

        // After middleware
        if (middleware.AfterMethod != null && !middleware.AfterMethod.IsAsync)
        {
            var methodInfo = middleware.AfterMethod;
            var args = string.Join(", ", methodInfo.Parameters.Select(p =>
                GenerateSingleMiddlewareParameterExpression(p, middleware)));
            source.AppendLine($"                middlewareInstance.{methodInfo.MethodName}({args});");
        }
        else if (middleware.AfterMethod != null && middleware.AfterMethod.IsAsync)
        {
            var methodInfo = middleware.AfterMethod;
            var args = string.Join(", ", methodInfo.Parameters.Select(p =>
                GenerateSingleMiddlewareParameterExpression(p, middleware)));
            source.AppendLine($"                await middlewareInstance.{methodInfo.MethodName}({args});");
        }

        if (IsVoidReturnType(handler.OriginalReturnTypeName))
        {
            // For void methods, don't return anything
        }
        else
        {
            source.AppendLine("                return handlerResult;");
        }

        source.AppendLine("            }");
        source.AppendLine("            catch (Exception ex)");
        source.AppendLine("            {");
        source.AppendLine("                exception = ex;");
        source.AppendLine("                throw;");
        source.AppendLine("            }");
        source.AppendLine("            finally");
        source.AppendLine("            {");

        // Finally middleware
        if (middleware.FinallyMethod != null)
        {
            var finallyParams = string.Join(", ", middleware.FinallyMethod.Parameters.Select(p =>
                GenerateSingleMiddlewareParameterExpression(p, middleware)));
            if (!middleware.FinallyMethod.IsAsync)
                source.AppendLine($"                middlewareInstance.{middleware.FinallyMethod.MethodName}({finallyParams});");
            else
                source.AppendLine($"                await middlewareInstance.{middleware.FinallyMethod.MethodName}({finallyParams});");
        }

        source.AppendLine("            }");
    }

    private static void GenerateAsyncMultipleMiddlewareExecution(StringBuilder source, HandlerInfo handler, List<MiddlewareInfo> middlewares)
    {
        // Generate middleware instances with descriptive names
        var middlewareVariableNames = new string[middlewares.Count];
        for (int i = 0; i < middlewares.Count; i++)
        {
            var variableName = GetMiddlewareVariableName(middlewares[i].MiddlewareTypeName);
            middlewareVariableNames[i] = variableName;
            source.AppendLine($"            var {variableName} = GetOrCreateMiddleware<{middlewares[i].MiddlewareTypeName}>(serviceProvider);");
        }

        source.AppendLine("            var beforeResults = new object?[" + middlewares.Count + "];");
        source.AppendLine(GetHandlerResultDeclaration(handler));
        source.AppendLine(GetExceptionDeclaration());
        source.AppendLine("            try");
        source.AppendLine("            {");

        // Before middleware (in order)
        for (int i = 0; i < middlewares.Count; i++)
        {
            var middleware = middlewares[i];
            var middlewareVar = middlewareVariableNames[i];
            if (middleware.BeforeMethod != null)
            {
                var methodInfo = middleware.BeforeMethod;
                var args = string.Join(", ", methodInfo.Parameters.Select(p =>
                    p.IsMessage ? "message" :
                    p.IsCancellationToken ? "cancellationToken" :
                    $"serviceProvider.GetRequiredService<{p.TypeName}>()"));
                if (methodInfo.IsAsync)
                    source.AppendLine($"                beforeResults[{i}] = await {middlewareVar}.{methodInfo.MethodName}({args});");
                else if (methodInfo.ReturnTypeName != "void")
                    source.AppendLine($"                beforeResults[{i}] = {middlewareVar}.{methodInfo.MethodName}({args});");
                else
                    source.AppendLine($"                {middlewareVar}.{methodInfo.MethodName}({args});");
            }

            // Only check for HandlerResult if the method can actually return one
            if (CanReturnHandlerResult(middleware.BeforeMethod))
            {
                source.AppendLine($"                if (beforeResults[{i}] is HandlerResult hr{i} && hr{i}.IsShortCircuited)");
                source.AppendLine("                {");
                if (IsVoidReturnType(handler.OriginalReturnTypeName))
                {
                    source.AppendLine("                    return;");
                }
                else
                {
                    source.AppendLine($"                    return {GetSafeCastExpression($"hr{i}", handler, useUnwrappedType: true)};");
                }
                source.AppendLine("                }");
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
            source.AppendLine("                var handlerInstance = GetOrCreateHandler(serviceProvider);");
            methodCall = GenerateMethodCall(handler, "handlerInstance", "message", "cancellationToken");
        }

        if (IsVoidReturnType(handler.OriginalReturnTypeName))
        {
            if (handler.IsAsync)
            {
                source.AppendLine($"                await {methodCall};");
            }
            else
            {
                source.AppendLine($"                {methodCall};");
            }
        }
        else
        {
            if (handler.IsAsync)
            {
                source.AppendLine($"                handlerResult = await {methodCall};");
            }
            else
            {
                source.AppendLine($"                handlerResult = {methodCall};");
            }
        }

        // After middleware (in order)
        for (int i = 0; i < middlewares.Count; i++)
        {
            var middleware = middlewares[i];
            var middlewareVar = middlewareVariableNames[i];
            if (middleware.AfterMethod != null)
            {
                var methodInfo = middleware.AfterMethod;
                var args = string.Join(", ", methodInfo.Parameters.Select(p =>
                    GenerateMiddlewareParameterExpression(p, middleware, i)));
                if (methodInfo.IsAsync)
                    source.AppendLine($"                await {middlewareVar}.{methodInfo.MethodName}({args});");
                else
                    source.AppendLine($"                {middlewareVar}.{methodInfo.MethodName}({args});");
            }
        }

        if (IsVoidReturnType(handler.OriginalReturnTypeName))
        {
            // For void methods, don't return anything
        }
        else
        {
            source.AppendLine($"                return ({GetUnwrappedReturnType(handler)})handlerResult;");
        }

        source.AppendLine("            }");
        source.AppendLine("            catch (Exception ex)");
        source.AppendLine("            {");
        source.AppendLine("                exception = ex;");
        source.AppendLine("                throw;");
        source.AppendLine("            }");
        source.AppendLine("            finally");
        source.AppendLine("            {");

        // Finally middleware (in reverse order)
        for (int i = middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = middlewares[i];
            if (middleware.FinallyMethod != null)
            {
                var methodInfo = middleware.FinallyMethod;
                var args = string.Join(", ", methodInfo.Parameters.Select(p =>
                    GenerateMiddlewareParameterExpression(p, middleware, i)));
                if (methodInfo.IsAsync)
                    source.AppendLine($"                await {middlewareVariableNames[i]}.{methodInfo.MethodName}({args});");
                else
                    source.AppendLine($"                {middlewareVariableNames[i]}.{methodInfo.MethodName}({args});");
            }
        }

        source.AppendLine("            }");
    }

    private static void GenerateSyncMultipleMiddlewareExecution(StringBuilder source, HandlerInfo handler, List<MiddlewareInfo> middlewares)
    {
        // Generate middleware instances with descriptive names
        var middlewareVariableNames = new string[middlewares.Count];
        for (int i = 0; i < middlewares.Count; i++)
        {
            var variableName = GetMiddlewareVariableName(middlewares[i].MiddlewareTypeName);
            middlewareVariableNames[i] = variableName;
            source.AppendLine($"            var {variableName} = GetOrCreateMiddleware<{middlewares[i].MiddlewareTypeName}>(serviceProvider);");
        }

        source.AppendLine("            var beforeResults = new object?[" + middlewares.Count + "];");
        source.AppendLine(GetHandlerResultDeclaration(handler));
        source.AppendLine(GetExceptionDeclaration());
        source.AppendLine("            try");
        source.AppendLine("            {");

        // Before middleware (in order)
        for (int i = 0; i < middlewares.Count; i++)
        {
            var middleware = middlewares[i];
            if (middleware.BeforeMethod != null)
            {
                var methodInfo = middleware.BeforeMethod;
                var args = string.Join(", ", methodInfo.Parameters.Select(p =>
                    p.IsMessage ? "message" :
                    p.IsCancellationToken ? "cancellationToken" :
                    $"serviceProvider.GetRequiredService<{p.TypeName}>()"));
                if (methodInfo.IsAsync)
                    source.AppendLine($"                beforeResults[{i}] = await {middlewareVariableNames[i]}.{methodInfo.MethodName}({args});");
                else if (methodInfo.ReturnTypeName != "void")
                    source.AppendLine($"                beforeResults[{i}] = {middlewareVariableNames[i]}.{methodInfo.MethodName}({args});");
                else
                    source.AppendLine($"                {middlewareVariableNames[i]}.{methodInfo.MethodName}({args});");
            }

            // Only check for HandlerResult if the method can actually return one
            if (CanReturnHandlerResult(middleware.BeforeMethod))
            {
                source.AppendLine($"                if (beforeResults[{i}] is HandlerResult hr{i} && hr{i}.IsShortCircuited)");
                source.AppendLine("                {");
                if (IsVoidReturnType(handler.OriginalReturnTypeName))
                {
                    source.AppendLine("                    return;");
                }
                else
                {
                    source.AppendLine($"                    return {GetSafeCastExpression($"hr{i}", handler)};");
                }
                source.AppendLine("                }");
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
            source.AppendLine("                var handlerInstance = GetOrCreateHandler(serviceProvider);");
            methodCall = GenerateMethodCall(handler, "handlerInstance", "message", "cancellationToken");
        }

        if (IsVoidReturnType(handler.OriginalReturnTypeName))
        {
            if (handler.IsAsync)
            {
                // In sync wrapper calling async handler, use GetAwaiter().GetResult()
                source.AppendLine($"                await {methodCall};");
            }
            else
            {
                source.AppendLine($"                {methodCall};");
            }
        }
        else
        {
            if (handler.IsAsync)
            {
                // In sync wrapper calling async handler, use GetAwaiter().GetResult()
                source.AppendLine($"                handlerResult = await {methodCall};");
            }
            else
            {
                source.AppendLine($"                handlerResult = {methodCall};");
            }
        }

        // After middleware (in order)
        for (int i = 0; i < middlewares.Count; i++)
        {
            var middleware = middlewares[i];
            if (middleware.AfterMethod != null && !middleware.AfterMethod.IsAsync)
            {
                var methodInfo = middleware.AfterMethod;
                var args = string.Join(", ", methodInfo.Parameters.Select(p =>
                    GenerateMiddlewareParameterExpression(p, middleware, i)));
                source.AppendLine($"                {middlewareVariableNames[i]}.{methodInfo.MethodName}({args});");
            }
            else if (middleware.AfterMethod != null && middleware.AfterMethod.IsAsync)
            {
                var methodInfo = middleware.AfterMethod;
                var args = string.Join(", ", methodInfo.Parameters.Select(p =>
                    GenerateMiddlewareParameterExpression(p, middleware, i)));
                source.AppendLine($"                await {middlewareVariableNames[i]}.{methodInfo.MethodName}({args});");
            }
        }

        if (IsVoidReturnType(handler.OriginalReturnTypeName))
        {
            // For void methods, don't return anything
        }
        else
        {
            source.AppendLine($"                return ({GetUnwrappedReturnType(handler)})handlerResult;");
        }

        source.AppendLine("            }");
        source.AppendLine("            catch (Exception ex)");
        source.AppendLine("            {");
        source.AppendLine("                exception = ex;");
        source.AppendLine("                throw;");
        source.AppendLine("            }");
        source.AppendLine("            finally");
        source.AppendLine("            {");

        // Finally middleware (in reverse order)
        for (int i = middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = middlewares[i];
            if (middleware.FinallyMethod != null)
            {
                var methodInfo = middleware.FinallyMethod;
                var args = string.Join(", ", methodInfo.Parameters.Select(p =>
                    GenerateMiddlewareParameterExpression(p, middleware, i)));
                if (methodInfo.IsAsync)
                    source.AppendLine($"                await {middlewareVariableNames[i]}.{methodInfo.MethodName}({args});");
                else
                    source.AppendLine($"                {middlewareVariableNames[i]}.{methodInfo.MethodName}({args});");
            }
        }

        source.AppendLine("            }");
    }

    private static void GenerateGetOrCreateMiddlewareMethod(StringBuilder source, List<MiddlewareInfo> middlewares)
    {
        source.AppendLine();
        source.AppendLine($"        private static readonly System.Collections.Concurrent.ConcurrentDictionary<System.Type, object> _middlewareCache = new();");
        source.AppendLine();
        source.AppendLine($"        private static T GetOrCreateMiddleware<T>(IServiceProvider serviceProvider) where T : class");
        source.AppendLine("        {");
        source.AppendLine("            // Check cache first - if it's there, it means it's not registered in DI");
        source.AppendLine("            if (_middlewareCache.TryGetValue(typeof(T), out var cachedInstance))");
        source.AppendLine("                return (T)cachedInstance;");
        source.AppendLine();
        source.AppendLine("            // Try to get from DI - if registered, always use DI (respects service lifetime)");
        source.AppendLine("            var middlewareFromDI = serviceProvider.GetService<T>();");
        source.AppendLine("            if (middlewareFromDI != null)");
        source.AppendLine("                return middlewareFromDI;");
        source.AppendLine();
        source.AppendLine("            // Not in DI, create and cache our own instance");
        source.AppendLine("            return (T)_middlewareCache.GetOrAdd(typeof(T), type => ");
        source.AppendLine("                ActivatorUtilities.CreateInstance<T>(serviceProvider));");
        source.AppendLine("        }");
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
                   "            object? handlerResult = null;\n" +
                   "#pragma warning restore CS0219";
        }

        var returnType = GetUnwrappedReturnType(handler);

        // Check if it's a reference type that should be nullable
        if (IsReferenceType(returnType))
        {
            return $"            {returnType}? handlerResult = null;";
        }

        return $"            {returnType} handlerResult = default({returnType});";
    }

    /// <summary>
    /// Gets the proper Exception variable declaration with nullable annotation.
    /// </summary>
    private static string GetExceptionDeclaration()
    {
        return "            Exception? exception = null;";
    }

    /// <summary>
    /// Gets a nullable-safe cast expression for HandlerResult.Value.
    /// </summary>
    private static string GetSafeCastExpression(string handlerResultVar, HandlerInfo handler, bool useUnwrappedType = false)
    {
        var returnType = useUnwrappedType || IsVoidReturnType(handler.OriginalReturnTypeName)
            ? GetUnwrappedReturnType(handler)
            : handler.OriginalReturnTypeName;

        if (IsReferenceType(returnType))
        {
            // For reference types, provide a null-coalescing fallback to satisfy non-nullable return types
            var defaultValue = GetDefaultValueForType(returnType);
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
        // Extract the simple type name (last part after the last dot)
        var simpleTypeName = middlewareTypeName.Contains('.')
            ? middlewareTypeName.Substring(middlewareTypeName.LastIndexOf('.') + 1)
            : middlewareTypeName;

        // Convert to camelCase
        if (string.IsNullOrEmpty(simpleTypeName))
            return "middleware";

        // Convert first character to lowercase
        return char.ToLowerInvariant(simpleTypeName[0]) + simpleTypeName.Substring(1);
    }

    /// <summary>
    /// Determines if a middleware method can return HandlerResult and should be checked for short-circuiting.
    /// </summary>
    private static bool CanReturnHandlerResult(MiddlewareMethodInfo? method)
    {
        if (method == null || method.ReturnTypeName == "void")
            return false;

        var returnType = method.ReturnTypeName;

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
    private static string GenerateMiddlewareParameterExpression(ParameterInfo parameter, MiddlewareInfo middleware, int middlewareIndex)
    {
        if (parameter.IsMessage)
            return "message";
        if (parameter.IsCancellationToken)
            return "cancellationToken";
        if (parameter.Name == "beforeResult")
            return $"beforeResults[{middlewareIndex}]";
        if (parameter.Name == "handlerResult")
            return "handlerResult";
        if (parameter.Name == "exception")
            return "exception";

        // Check if this parameter matches the Before method return type or a tuple field
        if (middleware.BeforeMethod != null)
        {
            var beforeReturnType = middleware.BeforeMethod.ReturnTypeName;

            // Direct type match
            if (parameter.TypeName == beforeReturnType)
            {
                return $"(({parameter.TypeName})beforeResults[{middlewareIndex}]!)";
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
                        // Generate tuple field access: ((TupleType)beforeResults[i]).Item1
                        return $"(({beforeReturnType})beforeResults[{middlewareIndex}]!).Item{fieldIndex + 1}";
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
        var content = tupleType.Trim('(', ')').Trim();

        // Split by comma, but be careful of nested generics
        var parts = new List<string>();
        var current = "";
        var depth = 0;

        for (int i = 0; i < content.Length; i++)
        {
            var c = content[i];
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
        if (!string.IsNullOrEmpty(current.Trim()))
        {
            parts.Add(current.Trim());
        }

        // Parse each part: "TypeName FieldName" or just "TypeName"
        foreach (var part in parts)
        {
            var tokens = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 1)
            {
                var type = tokens[0];
                var name = tokens.Length >= 2 ? tokens[1] : null;
                fields.Add((type, name));
            }
        }

        return fields;
    }

    /// <summary>
    /// Generates the appropriate parameter expression for single middleware methods,
    /// including handling tuple field extraction from Before method results.
    /// </summary>
    private static string GenerateSingleMiddlewareParameterExpression(ParameterInfo parameter, MiddlewareInfo middleware)
    {
        if (parameter.IsMessage)
            return "message";
        if (parameter.IsCancellationToken)
            return "cancellationToken";
        if (parameter.Name == "beforeResult")
            return "beforeResult";
        if (parameter.Name == "handlerResult")
            return "handlerResult";
        if (parameter.Name == "exception")
            return "exception";

        // Check if this parameter matches the Before method return type or a tuple field
        if (middleware.BeforeMethod != null)
        {
            var beforeReturnType = middleware.BeforeMethod.ReturnTypeName;

            // Direct type match
            if (parameter.TypeName == beforeReturnType)
            {
                return $"(({parameter.TypeName})beforeResult!)";
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
                        // Generate tuple field access: ((TupleType)beforeResult).Item1
                        return $"(({beforeReturnType})beforeResult!).Item{fieldIndex + 1}";
                    }
                }
            }
        }

        // Fall back to DI resolution
        return $"serviceProvider.GetRequiredService<{parameter.TypeName}>()";
    }
}
