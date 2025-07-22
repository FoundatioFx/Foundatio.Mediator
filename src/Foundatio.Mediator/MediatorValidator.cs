using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator;

internal static class MediatorValidator
{
    public static void ValidateHandlerConfiguration(List<HandlerInfo> handlers, SourceProductionContext context)
    {
        var handlersByMessageType = handlers.GroupBy(h => h.MessageTypeName);

        foreach (var group in handlersByMessageType)
        {
            var messageType = group.Key;
            var messageHandlers = group.ToList();

            // Check for multiple handlers (warning for publish, error for invoke if needed)
            if (messageHandlers.Count > 1)
            {
                var handlersList = string.Join(", ", messageHandlers.Select(h => $"{h.HandlerTypeName}.{h.MethodName}"));

                // Check if any handler has a return type (indicates invoke usage)
                var hasReturnTypeHandlers = messageHandlers.Any(h =>
                    h.ReturnTypeName != "void" &&
                    h.ReturnTypeName != "System.Threading.Tasks.Task" &&
                    !string.IsNullOrEmpty(h.ReturnTypeName));

                if (hasReturnTypeHandlers)
                {
                    // Error: Multiple handlers with return types not allowed for Invoke methods
                    var descriptor = new DiagnosticDescriptor(
                        "FMED001",
                        "Multiple handlers with return types not allowed",
                        "Message type '{0}' has multiple handlers with return types: {1}. Invoke<TResponse> methods require exactly one handler per message type. Use Publish methods for multiple handlers.",
                        "Foundatio.Mediator",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true);

                    var diagnostic = Diagnostic.Create(descriptor, Location.None, messageType, handlersList);
                    context.ReportDiagnostic(diagnostic);
                }
                else
                {
                    // Info: Multiple handlers found (valid for publish scenarios)
                    var descriptor = new DiagnosticDescriptor(
                        "FMED003",
                        "Multiple handlers found for message type",
                        "Message type '{0}' has multiple handlers: {1}. Invoke methods will use the first handler, Publish methods will call all handlers.",
                        "Foundatio.Mediator",
                        DiagnosticSeverity.Info,
                        isEnabledByDefault: true);

                    var diagnostic = Diagnostic.Create(descriptor, Location.None, messageType, handlersList);
                    context.ReportDiagnostic(diagnostic);
                }
            }

            // Note: We don't validate sync/async mismatch here at generation time
            // Instead, we'll generate all methods and let the compiler handle missing implementations
            // This allows consumers to use only async methods when appropriate
        }
    }

    public static void ValidateCallSites(List<HandlerInfo> handlers, List<MiddlewareInfo> middlewares, List<CallSiteInfo> callSites, SourceProductionContext context)
    {
        // Group handlers by message type for quick lookup
        var handlersByMessageType = handlers.GroupBy(h => h.MessageTypeName).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var callSite in callSites)
        {
            // Check if there are any handlers for this message type
            if (!handlersByMessageType.TryGetValue(callSite.MessageTypeName, out var messageHandlers) || messageHandlers.Count == 0)
            {
                // Only report missing handlers for Invoke calls, not Publish calls
                if (!callSite.IsPublish)
                {
                    var descriptor = new DiagnosticDescriptor(
                        "FMED004",
                        "No handler found for message type",
                        "No handler found for message type '{0}' in {1} call. Ensure a handler exists with the correct message type.",
                        "Foundatio.Mediator",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true);

                    var diagnostic = Diagnostic.Create(descriptor, callSite.Location, callSite.MessageTypeName, callSite.MethodName);
                    context.ReportDiagnostic(diagnostic);
                }
                continue;
            }

            // For Invoke calls, validate async/sync compatibility
            if (!callSite.IsPublish)
            {
                ValidateInvokeCall(callSite, messageHandlers, middlewares, context);
            }
            else // Publish calls
            {
                ValidatePublishCall(callSite, messageHandlers, context);
            }
        }
    }

    private static void ValidateInvokeCall(CallSiteInfo callSite, List<HandlerInfo> messageHandlers, List<MiddlewareInfo> middlewares, SourceProductionContext context)
    {
        // Check if multiple handlers exist for Invoke calls
        if (messageHandlers.Count > 1)
        {
            var handlersList = string.Join(", ", messageHandlers.Select(h => $"{h.HandlerTypeName}.{h.MethodName}"));
            var descriptor = new DiagnosticDescriptor(
                "FMED005",
                "Multiple handlers found for Invoke call",
                "Multiple handlers found for message type '{0}' in {1} call: {2}. Invoke methods require exactly one handler. Use Publish methods for multiple handlers.",
                "Foundatio.Mediator",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true);

            var diagnostic = Diagnostic.Create(descriptor, callSite.Location, callSite.MessageTypeName, callSite.MethodName, handlersList);
            context.ReportDiagnostic(diagnostic);
            return;
        }

        var handler = messageHandlers[0];

        // Check async/sync compatibility
        if (callSite.IsAsync && !handler.IsAsync)
        {
            // Check if there's async middleware that would justify the async call
            var applicableMiddlewares = GetApplicableMiddlewares(middlewares, handler);
            var hasAsyncMiddleware = applicableMiddlewares.Any(m => m.IsAsync);

            if (!hasAsyncMiddleware)
            {
                var descriptor = new DiagnosticDescriptor(
                    "FMED006",
                    "Async call with sync handler",
                    "Async {0} call for message type '{1}' but handler '{2}.{3}' is synchronous. Consider making the handler async or use the sync version of the mediator call.",
                    "Foundatio.Mediator",
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true);

                var diagnostic = Diagnostic.Create(descriptor, callSite.Location, callSite.MethodName, callSite.MessageTypeName, handler.HandlerTypeName, handler.MethodName);
                context.ReportDiagnostic(diagnostic);
            }
        }
        else if (!callSite.IsAsync && handler.IsAsync)
        {
            var descriptor = new DiagnosticDescriptor(
                "FMED007",
                "Sync call with async handler",
                "Sync {0} call for message type '{1}' but handler '{2}.{3}' is asynchronous. Use {0}Async instead or provide a synchronous handler.",
                "Foundatio.Mediator",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true);

            var diagnostic = Diagnostic.Create(descriptor, callSite.Location, callSite.MethodName, callSite.MessageTypeName, handler.HandlerTypeName, handler.MethodName);
            context.ReportDiagnostic(diagnostic);
        }

        // Check for sync call with async middleware
        if (!callSite.IsAsync && !handler.IsAsync)
        {
            var applicableMiddlewares = GetApplicableMiddlewares(middlewares, handler);
            var hasAsyncMiddleware = applicableMiddlewares.Any(m => m.IsAsync);

            if (hasAsyncMiddleware)
            {
                var asyncMiddlewares = applicableMiddlewares
                    .Where(m => m.IsAsync)
                    .Select(m => m.MiddlewareTypeName)
                    .ToList();
                var asyncMiddlewaresList = string.Join(", ", asyncMiddlewares);

                var descriptor = new DiagnosticDescriptor(
                    "FMED012",
                    "Sync call with async middleware",
                    "Sync {0} call for message type '{1}' but handler '{2}.{3}' has async middleware: {4}. Use {0}Async instead when async middleware is present.",
                    "Foundatio.Mediator",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true);

                var diagnostic = Diagnostic.Create(descriptor, callSite.Location, callSite.MethodName, callSite.MessageTypeName, handler.HandlerTypeName, handler.MethodName, asyncMiddlewaresList);
                context.ReportDiagnostic(diagnostic);
            }
        }

        // For generic Invoke<TResponse> calls, validate return type compatibility
        if (!string.IsNullOrEmpty(callSite.ExpectedResponseTypeName))
        {
            if (handler.ReturnTypeName == "void" ||
                handler.ReturnTypeName == "System.Threading.Tasks.Task" ||
                string.IsNullOrEmpty(handler.ReturnTypeName))
            {
                var descriptor = new DiagnosticDescriptor(
                    "FMED008",
                    "Handler doesn't return expected type",
                    "{0}<{1}> call for message type '{2}' expects return type '{1}' but handler '{3}.{4}' returns void. Handler must return the expected type or a compatible type.",
                    "Foundatio.Mediator",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true);

                var diagnostic = Diagnostic.Create(descriptor, callSite.Location, callSite.MethodName, callSite.ExpectedResponseTypeName, callSite.MessageTypeName, handler.HandlerTypeName, handler.MethodName);
                context.ReportDiagnostic(diagnostic);
            }
            // FMED009 diagnostic removed - let the mediator handle type compatibility at runtime
        }
    }

    private static void ValidatePublishCall(CallSiteInfo callSite, List<HandlerInfo> messageHandlers, SourceProductionContext context)
    {
        // For Publish calls, validate async/sync compatibility with ALL handlers
        var asyncHandlers = messageHandlers.Where(h => h.IsAsync).ToList();
        var syncHandlers = messageHandlers.Where(h => !h.IsAsync).ToList();

        if (callSite.IsAsync && syncHandlers.Count > 0)
        {
            var syncHandlersList = string.Join(", ", syncHandlers.Select(h => $"{h.HandlerTypeName}.{h.MethodName}"));
            var descriptor = new DiagnosticDescriptor(
                "FMED010",
                "Async publish with sync handlers",
                "Async {0} call for message type '{1}' but some handlers are synchronous: {2}. Consider making all handlers async for best performance.",
                "Foundatio.Mediator",
                DiagnosticSeverity.Info,
                isEnabledByDefault: true);

            var diagnostic = Diagnostic.Create(descriptor, callSite.Location, callSite.MethodName, callSite.MessageTypeName, syncHandlersList);
            context.ReportDiagnostic(diagnostic);
        }
        else if (!callSite.IsAsync && asyncHandlers.Count > 0)
        {
            var asyncHandlersList = string.Join(", ", asyncHandlers.Select(h => $"{h.HandlerTypeName}.{h.MethodName}"));
            var descriptor = new DiagnosticDescriptor(
                "FMED011",
                "Sync publish with async handlers",
                "Sync {0} call for message type '{1}' but some handlers are asynchronous: {2}. Use {0}Async instead for better performance, or provide synchronous handlers.",
                "Foundatio.Mediator",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true);

            var diagnostic = Diagnostic.Create(descriptor, callSite.Location, callSite.MethodName, callSite.MessageTypeName, asyncHandlersList);
            context.ReportDiagnostic(diagnostic);
        }
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
}
