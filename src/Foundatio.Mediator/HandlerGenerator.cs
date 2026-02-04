using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class HandlerGenerator
{
    public static void Execute(SourceProductionContext context, List<HandlerInfo> handlers, List<HandlerInfo> allHandlers, GeneratorConfiguration configuration)
    {
        if (handlers.Count == 0)
            return;

        Validate(context, handlers);

        foreach (var handler in handlers)
        {
            try
            {
                string wrapperClassName = GetHandlerClassName(handler);
                string hintName = $"{wrapperClassName}.g.cs";

                string source = GenerateHandler(handler, wrapperClassName, allHandlers, configuration, hintName);
                context.AddSource(hintName, source);
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

    public static string GenerateHandler(HandlerInfo handler, string wrapperClassName, List<HandlerInfo> allHandlers, GeneratorConfiguration configuration, string hintName)
    {
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, hintName);

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

        // Generate public entry point methods that include scope + handler invocation + cascading
        // Each method is a single async state machine to minimize allocations
        GenerateHandleMethod(source, handler, allHandlers, configuration);

        // Generate HandleItem2Async, HandleItem3Async, etc. for tuple handlers
        if (handler.ReturnType.IsTuple && handler.ReturnType.TupleItems.Length > 1)
        {
            GenerateHandleItemMethods(source, handler, allHandlers, configuration);
        }

        // Generate UntypedHandleAsync (uses PublishCascadingMessagesAsync for runtime dispatch)
        GenerateUntypedHandleMethod(source, handler, configuration);

        // Generate interceptor methods that delegate to HandleAsync/HandleItemNAsync
        GenerateInterceptorMethods(source, handler, allHandlers, configuration);

        if (!handler.IsStatic)
        {
            GenerateGetOrCreateHandler(source, handler);
        }

        // Generate middleware instantiation infrastructure
        GenerateMiddlewareInstantiation(source, handler);

        // Generate cached HandlerExecutionInfo if needed by middleware
        GenerateHandlerExecutionInfoField(source, handler);

        source.DecrementIndent();
        source.AppendLine("}");

        return source.ToString();
    }

    /// <summary>
    /// Generates the public HandleAsync/Handle method that is the single entry point.
    /// This method includes: scope creation, handler invocation (middleware + logging + OTel + handler call), and cascading.
    /// For tuple returns, this method returns the first tuple item and cascades the rest.
    /// </summary>
    private static void GenerateHandleMethod(IndentedStringBuilder source, HandlerInfo handler, List<HandlerInfo> allHandlers, GeneratorConfiguration configuration)
    {
        string methodName = GetHandlerMethodName(handler);
        bool isAsyncMethod = handler.IsAsync || handler.ReturnType.IsTuple;
        string returnTypeName = GetReturnTypeName(handler);
        string methodReturnType = GetMethodSignatureReturnType(isAsyncMethod, handler.ReturnType.IsVoid, returnTypeName);

        // Check if we can generate a pass-through method without async state machine
        // This applies when handler has no dependencies, no middleware, no cascading, and no OTel
        bool canSkipAsyncStateMachine = handler.CanSkipAsyncStateMachine && !configuration.OpenTelemetryEnabled;

        string asyncModifier = (isAsyncMethod && !canSkipAsyncStateMachine) ? "async " : "";

        source.AppendLine($"public static {asyncModifier}{methodReturnType} {methodName}(Foundatio.Mediator.IMediator mediator, {handler.MessageType.FullName} message, System.Threading.CancellationToken cancellationToken)")
              .AppendLine("{");

        source.IncrementIndent();

        // Check upfront if we have cascading handlers (for tuple returns)
        bool hasCascadingHandlers = false;
        List<TupleItemInfo> publishItems = [];
        TupleItemInfo? returnItem = null;

        if (handler.ReturnType.IsTuple && handler.ReturnType.TupleItems.Length > 1)
        {
            returnItem = handler.ReturnType.TupleItems.First();
            publishItems = handler.ReturnType.TupleItems.Skip(1).ToList();
            // With runtime DI lookup, always assume there might be handlers for cascaded messages
            hasCascadingHandlers = publishItems.Count > 0;
        }

        // Fast path: skip the async state machine when possible
        if (canSkipAsyncStateMachine)
        {
            bool hasCancellationToken = handler.Parameters.Any(p => p.Type.IsCancellationToken);
            string handlerArgs = hasCancellationToken ? "message, cancellationToken" : "message";

            // Determine accessor based on handler configuration
            string accessor;
            if (handler.IsStatic)
            {
                accessor = handler.FullName;
            }
            else if (handler.RequiresDIResolutionPerInvocation ||
                     string.Equals(handler.Lifetime, "Singleton", StringComparison.OrdinalIgnoreCase))
            {
                // Scoped/Transient/Singleton: resolve from DI (requires registration)
                source.AppendLine("var serviceProvider = (System.IServiceProvider)mediator;");
                source.AppendLine($"var handlerInstance = serviceProvider.GetRequiredService<{handler.FullName}>();");
                accessor = "handlerInstance";
            }
            else if (handler.RequiresConstructorInjection)
            {
                // Has constructor dependencies but no explicit DI lifetime: use ActivatorUtilities
                // This works without explicit registration and creates a fresh instance each time
                source.AppendLine("var serviceProvider = (System.IServiceProvider)mediator;");
                source.AppendLine($"var handlerInstance = ActivatorUtilities.CreateInstance<{handler.FullName}>(serviceProvider);");
                accessor = "handlerInstance";
            }
            else
            {
                // No explicit DI lifetime and no constructor deps - use lazy caching via GetOrCreateHandler
                source.AppendLine("var serviceProvider = (System.IServiceProvider)mediator;");
                source.AppendLine("var handlerInstance = GetOrCreateHandler(serviceProvider);");
                accessor = "handlerInstance";
            }

            if (handler.ReturnType.IsVoid)
            {
                if (handler.IsAsync)
                {
                    if (handler.ReturnType.IsValueTask)
                        source.AppendLine($"return {accessor}.{handler.MethodName}({handlerArgs});");
                    else
                        source.AppendLine($"return {accessor}.{handler.MethodName}({handlerArgs}).AsValueTask();");
                }
                else
                {
                    source.AppendLine($"{accessor}.{handler.MethodName}({handlerArgs});");
                    if (isAsyncMethod)
                    {
                        source.AppendLine("return default;");
                    }
                }
            }
            else
            {
                if (handler.IsAsync)
                {
                    if (handler.ReturnType.IsValueTask)
                        source.AppendLine($"return {accessor}.{handler.MethodName}({handlerArgs});");
                    else
                        source.AppendLine($"return {accessor}.{handler.MethodName}({handlerArgs}).AsValueTask();");
                }
                else if (isAsyncMethod)
                {
                    source.AppendLine($"return new System.Threading.Tasks.ValueTask<{returnTypeName}>({accessor}.{handler.MethodName}({handlerArgs}));");
                }
                else
                {
                    source.AppendLine($"return {accessor}.{handler.MethodName}({handlerArgs});");
                }
            }

            source.DecrementIndent();
            source.AppendLine("}");
            source.AppendLine();
            return;
        }

        // Get service provider directly from mediator - no scope creation
        // DI scope management is the caller's responsibility, not the mediator's
        source.AppendLine("var serviceProvider = (System.IServiceProvider)mediator;");

        // Emit the handler invocation code (middleware + logging + OTel + handler call)
        EmitHandlerInvocationCode(source, handler, configuration, "result");

        // For tuple returns, cascade non-first items
        if (handler.ReturnType.IsTuple && handler.ReturnType.TupleItems.Length > 1 && publishItems.Count > 0)
        {
            source.AppendLine();

            if (hasCascadingHandlers)
            {
                string strategy = configuration.NotificationPublisher ?? "ForeachAwait";
                GenerateCascadingHandlerCalls(source, publishItems, allHandlers, strategy);
                source.AppendLine();
            }

            source.AppendLine($"return result.{returnItem!.Value.Name};");
        }
        else if (handler.HasReturnValue)
        {
            source.AppendLine("return result;");
        }

        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();
    }

    /// <summary>
    /// Generates HandleItem2/HandleItem2Async, HandleItem3/HandleItem3Async, etc. for tuple handlers.
    /// Each method returns the Nth tuple item and cascades the rest.
    /// Methods are sync if the handler is sync and there are no async cascading handlers.
    /// </summary>
    private static void GenerateHandleItemMethods(IndentedStringBuilder source, HandlerInfo handler, List<HandlerInfo> allHandlers, GeneratorConfiguration configuration)
    {
        if (!handler.ReturnType.IsTuple || handler.ReturnType.TupleItems.Length < 2)
            return;

        var tupleItems = handler.ReturnType.TupleItems;
        string strategy = configuration.NotificationPublisher ?? "ForeachAwait";

        // Generate a method for each non-first tuple item (index >= 1)
        for (int targetIndex = 1; targetIndex < tupleItems.Length; targetIndex++)
        {
            var targetItem = tupleItems[targetIndex];

            // Get all items except the target item (those need to be cascaded)
            var itemsToCascade = tupleItems
                .Select((item, index) => (item, index))
                .Where(x => x.index != targetIndex)
                .Select(x => x.item)
                .ToList();

            // With runtime DI lookup, always assume there might be handlers for cascaded messages
            bool hasCascadingHandlers = itemsToCascade.Count > 0;

            // Method is async only if handler is async or there are cascading handlers
            bool isAsyncMethod = handler.IsAsync || hasCascadingHandlers;

            string methodName = GetHandlerItemMethodName(handler, targetIndex, isAsyncMethod);
            string returnTypeName = GetReturnTypeName(handler, targetIndex);
            string methodReturnType = GetMethodSignatureReturnType(isAsyncMethod, isVoid: false, returnTypeName);
            string asyncModifier = isAsyncMethod ? "async " : "";

            source.AppendLine($"public static {asyncModifier}{methodReturnType} {methodName}(Foundatio.Mediator.IMediator mediator, {handler.MessageType.FullName} message, System.Threading.CancellationToken cancellationToken)");
            source.AppendLine("{");
            source.IncrementIndent();

            // Get service provider directly from mediator - no scope creation
            source.AppendLine("var serviceProvider = (System.IServiceProvider)mediator;");

            // Emit the handler invocation code with the target tuple index
            EmitHandlerInvocationCode(source, handler, configuration, "result", "message", targetIndex);

            if (hasCascadingHandlers)
            {
                source.AppendLine();
                GenerateCascadingHandlerCalls(source, itemsToCascade, allHandlers, strategy);
            }

            source.AppendLine($"return result.{targetItem.Name};");
            source.DecrementIndent();
            source.AppendLine("}");
            source.AppendLine();
        }
    }

    /// <summary>
    /// Emits the handler invocation code: middleware + logging + OTel + handler call.
    /// This is the core handler logic that is duplicated into each entry point method.
    /// </summary>
    /// <param name="source">The string builder to emit code to.</param>
    /// <param name="handler">The handler info.</param>
    /// <param name="configuration">Generator configuration.</param>
    /// <param name="resultVar">Variable name to store the handler result.</param>
    /// <param name="messageVar">Variable name containing the typed message (default: "message").</param>
    /// <param name="targetTupleIndex">For tuple return types, the index of the tuple item this method returns (0 = Item1, 1 = Item2, etc.). -1 for non-tuple handlers.</param>
    private static void EmitHandlerInvocationCode(IndentedStringBuilder source, HandlerInfo handler, GeneratorConfiguration configuration, string resultVar, string messageVar = "message", int targetTupleIndex = 0, bool isUntypedMethod = false)
    {
        var variables = new Dictionary<string, string> { ["System.IServiceProvider"] = "serviceProvider" };

        // Build middleware lists - separate Execute middleware from Before/After/Finally
        var executeMiddleware = handler.HasExecuteMiddleware
            ? handler.Middleware.Where(m => m.ExecuteMethod != null).OrderBy(m => m.Order ?? 0).ToList()
            : [];
        var beforeMiddleware = handler.HasBeforeMiddleware
            ? handler.Middleware.Where(m => m.BeforeMethod != null).Select(m => (Method: m.BeforeMethod!.Value, Middleware: m)).ToList()
            : [];
        var afterMiddleware = handler.HasAfterMiddleware
            ? handler.Middleware.Where(m => m.AfterMethod != null).Reverse().Select(m => (Method: m.AfterMethod!.Value, Middleware: m)).ToList()
            : [];
        var finallyMiddleware = handler.HasFinallyMiddleware
            ? handler.Middleware.Where(m => m.FinallyMethod != null).Reverse().Select(m => (Method: m.FinallyMethod!.Value, Middleware: m)).ToList()
            : [];

        bool requiresTryCatch = handler.RequiresTryCatch || configuration.OpenTelemetryEnabled;

        // Setup phase: OTel, handler info, middleware instances, result variables
        EmitOpenTelemetrySetup(source, handler, configuration, variables);
        EmitHandlerExecutionInfo(source, handler, variables);
        EmitMiddlewareInstances(source, handler);
        EmitBeforeMiddlewareResultVariables(source, beforeMiddleware, variables);
        EmitHandlerResultVariable(source, handler, resultVar);

        // Check if we have Execute middleware - if so, wrap the entire pipeline
        if (executeMiddleware.Count > 0)
        {
            EmitExecuteMiddlewareChain(source, handler, executeMiddleware, beforeMiddleware, afterMiddleware, finallyMiddleware, configuration, variables, resultVar, messageVar, targetTupleIndex);
        }
        else
        {
            // Original code path - no Execute middleware
            EmitPipelineCode(source, handler, beforeMiddleware, afterMiddleware, finallyMiddleware, configuration, variables, resultVar, messageVar, targetTupleIndex, requiresTryCatch, isUntypedMethod: isUntypedMethod);
        }
    }

    /// <summary>
    /// Emits the Execute middleware chain that wraps the entire pipeline.
    /// </summary>
    private static void EmitExecuteMiddlewareChain(
        IndentedStringBuilder source,
        HandlerInfo handler,
        List<MiddlewareInfo> executeMiddleware,
        List<(MiddlewareMethodInfo Method, MiddlewareInfo Middleware)> beforeMiddleware,
        List<(MiddlewareMethodInfo Method, MiddlewareInfo Middleware)> afterMiddleware,
        List<(MiddlewareMethodInfo Method, MiddlewareInfo Middleware)> finallyMiddleware,
        GeneratorConfiguration configuration,
        Dictionary<string, string> variables,
        string resultVar,
        string messageVar,
        int targetTupleIndex)
    {
        bool requiresTryCatch = handler.RequiresTryCatch || configuration.OpenTelemetryEnabled;

        // Build innermost delegate containing the entire Before → Handler → After → Finally pipeline
        source.AppendLine("Foundatio.Mediator.HandlerExecutionDelegate pipelineDelegate = async () =>");
        source.AppendLine("{");
        source.IncrementIndent();

        // Create inner variables for the pipeline scope
        var innerVariables = new Dictionary<string, string>(variables);

        // Emit result variable for inner scope
        if (handler.HasReturnValue)
        {
            bool allowNull = handler.ReturnType.IsNullable || handler.ReturnType.IsReferenceType;
            source.AppendLine($"{handler.ReturnType.UnwrappedFullName}{(allowNull ? "?" : "")} innerResult = default;");
        }

        // Emit the full pipeline inside the delegate - isUntypedMethod is false here because we're inside a delegate that returns object?
        EmitPipelineCode(source, handler, beforeMiddleware, afterMiddleware, finallyMiddleware, configuration, innerVariables, "innerResult", messageVar, targetTupleIndex, requiresTryCatch, insideExecuteDelegate: true, isUntypedMethod: false);

        source.AppendLine(handler.HasReturnValue ? "return innerResult;" : "return null;");
        source.DecrementIndent();
        source.AppendLine("};");
        source.AppendLine();

        // Wrap with each Execute middleware (reverse order for proper nesting - innermost first)
        var reversed = executeMiddleware.AsEnumerable().Reverse().ToList();
        for (int i = 0; i < reversed.Count; i++)
        {
            var m = reversed[i];
            string accessor = m.IsStatic ? m.FullName : m.Identifier.ToCamelCase();
            string currentDelegate = i == 0 ? "pipelineDelegate" : $"wrapped{i - 1}";
            string nextDelegate = $"wrapped{i}";

            // Build parameters for Execute method
            string parameters = BuildExecuteParameters(source, m.ExecuteMethod!.Value.Parameters, variables, messageVar, currentDelegate);

            string asyncModifier = m.ExecuteMethod!.Value.IsAsync ? "await " : "";
            source.AppendLine($"Foundatio.Mediator.HandlerExecutionDelegate {nextDelegate} = async () =>");
            source.AppendLine("{");
            source.AppendLine($"    return {asyncModifier}{accessor}.{m.ExecuteMethod!.Value.MethodName}({parameters});");
            source.AppendLine("};");
        }

        // Execute the outermost delegate and cast result
        string finalDelegate = reversed.Count > 0 ? $"wrapped{reversed.Count - 1}" : "pipelineDelegate";
        if (handler.HasReturnValue)
        {
            source.AppendLine($"{resultVar} = ({handler.ReturnType.UnwrappedFullName})(await {finalDelegate}())!;");
        }
        else
        {
            source.AppendLine($"await {finalDelegate}();");
        }
    }

    /// <summary>
    /// Builds the parameters for an Execute middleware method call.
    /// </summary>
    private static string BuildExecuteParameters(IndentedStringBuilder source, EquatableArray<ParameterInfo> parameters, Dictionary<string, string> variables, string messageVar, string delegateVar)
    {
        var parameterValues = new List<string>();

        foreach (var param in parameters)
        {
            if (param.IsMessageParameter)
            {
                parameterValues.Add(messageVar);
            }
            else if (param.Type.FullName == WellKnownTypes.HandlerExecutionDelegate)
            {
                parameterValues.Add(delegateVar);
            }
            else if (param.Type.IsCancellationToken)
            {
                parameterValues.Add("cancellationToken");
            }
            else if (param.Type.IsHandlerExecutionInfo)
            {
                parameterValues.Add("handlerExecutionInfo");
            }
            else if (variables.TryGetValue(param.Type.QualifiedName, out string? qualifiedVariableName))
            {
                parameterValues.Add(qualifiedVariableName);
            }
            else if (variables.TryGetValue(param.Type.FullName, out string? variableName))
            {
                parameterValues.Add(variableName);
            }
            else
            {
                // DI parameter
                parameterValues.Add($"serviceProvider.GetRequiredService<{param.Type.FullName}>()");
            }
        }

        return String.Join(", ", parameterValues);
    }

    /// <summary>
    /// Emits the core pipeline code: Before middleware → Handler → After middleware, with optional try-catch-finally.
    /// </summary>
    /// <param name="insideExecuteDelegate">When true, we're inside an Execute middleware delegate and short-circuit
    /// returns should return the full tuple type (not just one item) since the delegate returns object?.</param>
    /// <param name="isUntypedMethod">When true, we're generating UntypedHandleAsync which returns ValueTask&lt;object?&gt;,
    /// so void handlers should return null instead of just return.</param>
    private static void EmitPipelineCode(
        IndentedStringBuilder source,
        HandlerInfo handler,
        List<(MiddlewareMethodInfo Method, MiddlewareInfo Middleware)> beforeMiddleware,
        List<(MiddlewareMethodInfo Method, MiddlewareInfo Middleware)> afterMiddleware,
        List<(MiddlewareMethodInfo Method, MiddlewareInfo Middleware)> finallyMiddleware,
        GeneratorConfiguration configuration,
        Dictionary<string, string> variables,
        string resultVar,
        string messageVar,
        int targetTupleIndex,
        bool requiresTryCatch,
        bool insideExecuteDelegate = false,
        bool isUntypedMethod = false)
    {
        // Main execution with optional try-catch-finally
        if (requiresTryCatch)
        {
            source.AppendLine("""
                System.Exception? exception = null;

                try
                {
                """);
            source.IncrementIndent();
            variables["System.Exception"] = "exception";
        }

        EmitBeforeMiddlewareCalls(source, beforeMiddleware, handler, variables, messageVar, targetTupleIndex, insideExecuteDelegate, isUntypedMethod);
        EmitHandlerInvocation(source, handler, variables, resultVar, messageVar);
        EmitAfterMiddlewareCalls(source, afterMiddleware, variables, messageVar);

        if (requiresTryCatch)
        {
            source.DecrementIndent();
            EmitCatchAndFinallyBlocks(source, configuration, finallyMiddleware, variables, messageVar);
        }
    }

    private static void EmitOpenTelemetrySetup(IndentedStringBuilder source, HandlerInfo handler, GeneratorConfiguration configuration, Dictionary<string, string> variables)
    {
        if (!configuration.OpenTelemetryEnabled)
            return;

        source.AppendLine($"using var activity = MediatorActivitySource.Instance.StartActivity(\"{handler.MessageType.Identifier}\");");
        source.AppendLine($"activity?.SetTag(\"messaging.system\", \"Foundatio.Mediator\");");
        source.AppendLine($"activity?.SetTag(\"messaging.message.type\", \"{handler.MessageType.FullName}\");");
        variables["System.Diagnostics.Activity"] = "activity";
    }

    private static void EmitHandlerExecutionInfo(IndentedStringBuilder source, HandlerInfo handler, Dictionary<string, string> variables)
    {
        if (!handler.RequiresHandlerExecutionInfo)
            return;

        // Use the cached static field instead of creating new instance each time
        source.AppendLine("var handlerExecutionInfo = GetOrCreateHandlerExecutionInfo();");
        variables[WellKnownTypes.HandlerExecutionInfo] = "handlerExecutionInfo";
    }

    private static void GenerateHandlerExecutionInfoField(IndentedStringBuilder source, HandlerInfo handler)
    {
        if (!handler.RequiresHandlerExecutionInfo)
            return;

        var paramTypes = string.Join(", ", handler.Parameters.Select(p => $"typeof({p.Type.FullName})"));
        string paramTypesArray = string.IsNullOrEmpty(paramTypes) ? "System.Type.EmptyTypes" : $"new[] {{ {paramTypes} }}";

        source.AppendLine()
              .AppendLines($$"""
                private static Foundatio.Mediator.HandlerExecutionInfo? _cachedHandlerExecutionInfo;

                [DebuggerStepThrough]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static Foundatio.Mediator.HandlerExecutionInfo GetOrCreateHandlerExecutionInfo()
                {
                    return _cachedHandlerExecutionInfo ??= new Foundatio.Mediator.HandlerExecutionInfo(typeof({{handler.FullName}}), typeof({{handler.FullName}}).GetMethod("{{handler.MethodName}}", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance, null, {{paramTypesArray}}, null)!);
                }
                """);
    }

    private static void EmitMiddlewareInstances(IndentedStringBuilder source, HandlerInfo handler)
    {
        if (!handler.RequiresMiddlewareInstances)
            return;

        foreach (var m in handler.Middleware.Where(m => !m.IsStatic))
        {
            string varName = m.Identifier.ToCamelCase();

            // Scoped/Transient/Singleton: Always resolve from DI
            // For Singleton, the user explicitly wants DI to manage the instance
            if (m.RequiresDIResolutionPerInvocation ||
                string.Equals(m.Lifetime, "Singleton", StringComparison.OrdinalIgnoreCase))
            {
                source.AppendLine($"var {varName} = serviceProvider.GetRequiredService<{m.FullName}>();");
            }
            else
            {
                // No explicit lifetime (None/Default) and default is None - use caching
                // No constructor deps: use new() and cache
                // With constructor deps: use ActivatorUtilities and cache
                source.AppendLine($"var {varName} = GetOrCreate{m.Identifier}(serviceProvider);");
            }
        }
        source.AppendLine();
    }

    private static void EmitBeforeMiddlewareResultVariables(
        IndentedStringBuilder source,
        List<(MiddlewareMethodInfo Method, MiddlewareInfo Middleware)> beforeMiddleware,
        Dictionary<string, string> variables)
    {
        foreach (var m in beforeMiddleware.Where(m => m.Method.HasReturnValue))
        {
            bool allowNull = m.Method.ReturnType.IsNullable || m.Method.ReturnType.IsReferenceType;
            string defaultValue = allowNull ? "null" : "default";
            string nullableMarker = allowNull ? "?" : "";
            string resultVarName = $"{m.Middleware.Identifier.ToCamelCase()}Result";

            source.AppendLine($"{m.Method.ReturnType.UnwrappedFullName}{nullableMarker} {resultVarName} = {defaultValue};");

            variables[m.Method.ReturnType.FullName] = $"{resultVarName}{(allowNull ? "!" : "")}";
            if (m.Method.ReturnType.IsTuple)
            {
                foreach (var tupleItem in m.Method.ReturnType.TupleItems)
                {
                    variables[tupleItem.TypeFullName] = $"{resultVarName}.{tupleItem.Name}{(allowNull ? "!" : "")}";
                }
            }
        }
        source.AppendLineIf(beforeMiddleware.Any(m => m.Method.HasReturnValue));
    }

    private static void EmitHandlerResultVariable(IndentedStringBuilder source, HandlerInfo handler, string resultVar)
    {
        if (!handler.HasReturnValue)
            return;

        bool allowNull = handler.ReturnType.IsNullable || handler.ReturnType.IsReferenceType;
        source.AppendLine($"{handler.ReturnType.UnwrappedFullName}{(allowNull ? "?" : "")} {resultVar} = default;");
    }

    private static void EmitBeforeMiddlewareCalls(
        IndentedStringBuilder source,
        List<(MiddlewareMethodInfo Method, MiddlewareInfo Middleware)> beforeMiddleware,
        HandlerInfo handler,
        Dictionary<string, string> variables,
        string messageVar,
        int targetTupleIndex,
        bool insideExecuteDelegate = false,
        bool isUntypedMethod = false)
    {
        foreach (var m in beforeMiddleware)
        {
            string asyncModifier = m.Method.IsAsync ? "await " : "";
            string result = m.Method.ReturnType.IsVoid ? "" : $"{m.Middleware.Identifier.ToCamelCase()}Result = ";
            string accessor = m.Middleware.IsStatic ? m.Middleware.FullName : m.Middleware.Identifier.ToCamelCase();
            string parameters = BuildParameters(source, m.Method.Parameters, variables, messageVar);

            source.AppendLine($"{result}{asyncModifier}{accessor}.{m.Method.MethodName}({parameters});");

            if (m.Method.ReturnType.IsHandlerResult)
            {
                EmitShortCircuitCheck(source, m, handler, targetTupleIndex, insideExecuteDelegate, isUntypedMethod);
            }
        }
        source.AppendLineIf(beforeMiddleware.Any());
    }

    private static void EmitShortCircuitCheck(
        IndentedStringBuilder source,
        (MiddlewareMethodInfo Method, MiddlewareInfo Middleware) m,
        HandlerInfo handler,
        int targetTupleIndex,
        bool insideExecuteDelegate = false,
        bool isUntypedMethod = false)
    {
        string resultVarName = $"{m.Middleware.Identifier.ToCamelCase()}Result";
        string valueAccess = m.Method.ReturnType.IsGeneric ? $"{resultVarName}.Value" : $"{resultVarName}.Value!";
        string shortCircuitValue = ComputeShortCircuitValue(m.Method.ReturnType, handler.ReturnType, valueAccess);

        source.AppendLine($"if ({resultVarName}.IsShortCircuited)");
        source.AppendLine("{");

        if (handler.HasReturnValue)
        {
            if (handler.ReturnType.IsTuple && !insideExecuteDelegate)
            {
                // When NOT inside an Execute delegate, return just the target tuple item
                var targetItem = handler.ReturnType.TupleItems[targetTupleIndex];
                source.AppendLine($"    return {shortCircuitValue}.{targetItem.Field};");
            }
            else
            {
                // When inside an Execute delegate (or non-tuple), return the full value
                // since the delegate returns object? which will be cast to the full return type
                source.AppendLine($"    return {shortCircuitValue};");
            }
        }
        else
        {
            // For void handlers: UntypedHandleAsync returns ValueTask<object?> so needs "return null"
            // Typed HandleAsync returns ValueTask so needs just "return"
            source.AppendLine(isUntypedMethod ? "    return null;" : "    return;");
        }

        source.AppendLine("}");
    }

    private static string ComputeShortCircuitValue(TypeSymbolInfo middlewareReturnType, TypeSymbolInfo handlerReturnType, string valueAccess)
    {
        if (handlerReturnType.IsResult)
        {
            return middlewareReturnType.IsGeneric
                ? $"{valueAccess} is Foundatio.Mediator.Result __r ? ({handlerReturnType.UnwrappedFullName})__r : ({handlerReturnType.UnwrappedFullName}?){valueAccess}!"
                : $"({valueAccess} is Foundatio.Mediator.Result __r ? ({handlerReturnType.UnwrappedFullName})__r : ({handlerReturnType.UnwrappedFullName}?)({handlerReturnType.UnwrappedFullName}){valueAccess})!";
        }

        if (handlerReturnType.IsTuple)
        {
            var tupleItems = handlerReturnType.TupleItems;
            var tupleElements = new List<string>();

            for (int i = 0; i < tupleItems.Length; i++)
            {
                var item = tupleItems[i];
                if (i == 0)
                {
                    string convertedValue = middlewareReturnType.IsGeneric
                        ? $"({valueAccess} is Foundatio.Mediator.Result __r ? ({item.TypeFullName})__r : ({item.TypeFullName}?){valueAccess})!"
                        : $"({valueAccess} is Foundatio.Mediator.Result __r ? ({item.TypeFullName})__r : ({item.TypeFullName}?)({item.TypeFullName}){valueAccess})!";
                    tupleElements.Add(convertedValue);
                }
                else
                {
                    tupleElements.Add(item.IsNullable ? $"({item.TypeFullName})null" : $"default({item.TypeFullName})!");
                }
            }

            return $"({String.Join(", ", tupleElements)})";
        }

        return middlewareReturnType.IsGeneric ? valueAccess : $"({handlerReturnType.UnwrappedFullName}){valueAccess}";
    }

    private static void EmitHandlerInvocation(
        IndentedStringBuilder source,
        HandlerInfo handler,
        Dictionary<string, string> variables,
        string resultVar,
        string messageVar)
    {
        string asyncModifier = handler.ReturnType.IsTask ? "await " : "";
        string result = handler.ReturnType.IsVoid ? "" : $"{resultVar} = ";
        string parameters = BuildParameters(source, handler.Parameters, variables, messageVar);

        // Determine handler accessor - must match GenerateGetOrCreateHandler logic
        string accessor;
        if (handler.IsStatic)
        {
            accessor = handler.FullName;
        }
        else if (handler.RequiresDIResolutionPerInvocation ||
                 string.Equals(handler.Lifetime, "Singleton", StringComparison.OrdinalIgnoreCase))
        {
            // Scoped/Transient/Singleton: resolve from DI (requires registration)
            source.AppendLine($"var handlerInstance = serviceProvider.GetRequiredService<{handler.FullName}>();");
            accessor = "handlerInstance";
        }
        else if (handler.RequiresConstructorInjection)
        {
            // Has constructor dependencies but no explicit DI lifetime: use ActivatorUtilities
            // This works without explicit registration and creates a fresh instance each time
            source.AppendLine($"var handlerInstance = ActivatorUtilities.CreateInstance<{handler.FullName}>(serviceProvider);");
            accessor = "handlerInstance";
        }
        else
        {
            // No explicit DI lifetime and no constructor deps - use GetOrCreateHandler with lazy caching
            source.AppendLine("var handlerInstance = GetOrCreateHandler(serviceProvider);");
            accessor = "handlerInstance";
        }

        source.AppendLine($"{result}{asyncModifier}{accessor}.{handler.MethodName}({parameters});");

        // Update variables with handler result for after/finally middleware
        if (handler.HasReturnValue)
        {
            variables[handler.ReturnType.FullName] = resultVar;

            if (handler.ReturnType.QualifiedName != handler.ReturnType.FullName)
            {
                variables[handler.ReturnType.QualifiedName] = resultVar;
            }

            if (handler.ReturnType.IsResult)
            {
                variables[WellKnownTypes.Result] = $"{resultVar}!";
            }

            if (handler.ReturnType.IsTuple)
            {
                foreach (var tupleItem in handler.ReturnType.TupleItems)
                {
                    variables[tupleItem.TypeFullName] = $"{resultVar}.{tupleItem.Name}";

                    if (tupleItem.TypeFullName.StartsWith(WellKnownTypes.ResultOfT.Replace("`1", "<")))
                    {
                        variables[WellKnownTypes.Result] = $"{resultVar}.{tupleItem.Name}!";
                    }
                }
            }
        }
    }

    private static void EmitAfterMiddlewareCalls(
        IndentedStringBuilder source,
        List<(MiddlewareMethodInfo Method, MiddlewareInfo Middleware)> afterMiddleware,
        Dictionary<string, string> variables,
        string messageVar)
    {
        foreach (var m in afterMiddleware)
        {
            string asyncModifier = m.Method.IsAsync ? "await " : "";
            string accessor = m.Middleware.IsStatic ? m.Middleware.FullName : m.Middleware.Identifier.ToCamelCase();
            string parameters = BuildParameters(source, m.Method.Parameters, variables, messageVar);

            source.AppendLine($"{asyncModifier}{accessor}.{m.Method.MethodName}({parameters});");
        }
        source.AppendLineIf(afterMiddleware.Any());
    }

    private static void EmitCatchAndFinallyBlocks(
        IndentedStringBuilder source,
        GeneratorConfiguration configuration,
        List<(MiddlewareMethodInfo Method, MiddlewareInfo Middleware)> finallyMiddleware,
        Dictionary<string, string> variables,
        string messageVar)
    {
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

        foreach (var m in finallyMiddleware)
        {
            string asyncModifier = m.Method.IsAsync ? "await " : "";
            string accessor = m.Method.IsStatic ? m.Middleware.FullName : m.Middleware.Identifier.ToCamelCase();
            string parameters = BuildParameters(source, m.Method.Parameters, variables, messageVar);

            source.AppendLine($"{asyncModifier}{accessor}.{m.Method.MethodName}({parameters});");
        }

        source.DecrementIndent();
        source.AppendLine("}");
    }

    /// <summary>
    /// Generates the UntypedHandleAsync method that embeds handler code and uses PublishCascadingMessagesAsync for tuples.
    /// This is used for runtime dispatch when the response type isn't known at compile time.
    /// </summary>
    private static void GenerateUntypedHandleMethod(IndentedStringBuilder source, HandlerInfo handler, GeneratorConfiguration configuration)
    {
        bool isAsyncMethod = handler.IsAsync || handler.ReturnType.IsTuple;

        source.AppendLine(isAsyncMethod
            ? "public static async ValueTask<object?> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)"
            : "public static object? UntypedHandle(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)");

        source.AppendLine("{");
        source.IncrementIndent();

        // Cast message to typed message
        source.AppendLine($"var typedMessage = ({handler.MessageType.FullName})message;");

        // Get service provider directly from mediator - no scope creation
        source.AppendLine("var serviceProvider = (System.IServiceProvider)mediator;");

        // Emit the handler invocation code - use "typedMessage" since we cast the object message above
        EmitHandlerInvocationCode(source, handler, configuration, "result", "typedMessage", isUntypedMethod: true);

        // For tuple returns, use PublishCascadingMessagesAsync for runtime dispatch
        if (handler.ReturnType.IsTuple)
        {
            source.AppendLine("return await mediator.PublishCascadingMessagesAsync(result, responseType);");
        }
        else if (handler.HasReturnValue)
        {
            source.AppendLine("if (responseType == null)");
            source.AppendLine("{");
            source.AppendLine("    return null;");
            source.AppendLine("}");

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

            source.AppendLine("return result;");
        }
        else
        {
            source.AppendLine("return null;");
        }

        source.DecrementIndent();
        source.AppendLine("}");
    }

    /// <summary>
    /// Generates interceptor methods that delegate to HandleAsync/HandleItemNAsync.
    /// Interceptors are thin wrappers that just cast the message and call the appropriate method.
    /// </summary>
    private static void GenerateInterceptorMethods(IndentedStringBuilder source, HandlerInfo handler, List<HandlerInfo> allHandlers, GeneratorConfiguration configuration)
    {
        if (!configuration.InterceptorsEnabled)
            return;

        // group by mediator method, response type, whether it uses IRequest<T> overload, and whether it's async
        var callSiteGroups = handler.CallSites
            .GroupBy(cs => new { cs.MethodName, cs.ResponseType, cs.UsesIRequestOverload, cs.IsAsyncMethod })
            .ToList();

        int methodCounter = 0;
        foreach (var group in callSiteGroups)
        {
            var key = group.Key;
            var groupCallSites = group.ToList();

            source.AppendLine();
            GenerateInterceptorMethod(source, handler, allHandlers, key.MethodName, key.ResponseType, key.UsesIRequestOverload, key.IsAsyncMethod, groupCallSites, methodCounter++);
        }
    }

    /// <summary>
    /// Generates a single interceptor method.
    /// For non-fast-path handlers, the interceptor simply casts the message and calls HandleAsync/HandleItemNAsync.
    /// The HandleAsync method handles scope creation, logging, middleware, and cascading internally.
    /// </summary>
    private static void GenerateInterceptorMethod(IndentedStringBuilder source, HandlerInfo handler, List<HandlerInfo> allHandlers, string methodName, TypeSymbolInfo responseType, bool usesIRequestOverload, bool isAsyncMethod, List<CallSiteInfo> callSites, int methodIndex)
    {
        string interceptorMethod = $"Intercept{methodName}{methodIndex}";

        foreach (var callSite in callSites)
        {
            source.AppendLine($"[System.Runtime.CompilerServices.InterceptsLocation({callSite.Location.Version}, \"{callSite.Location.Data}\")] // {callSite.Location.DisplayLocation}");
        }

        var returnInfo = InterceptorCodeEmitter.ComputeReturnInfo(handler, responseType, isAsyncMethod);

        // Use IRequest<TResponse> parameter type when intercepting the IRequest overload
        string messageParamType = usesIRequestOverload
            ? $"Foundatio.Mediator.IRequest<{responseType.UnwrappedFullName}>"
            : "object";
        string parameters = $"this Foundatio.Mediator.IMediator mediator, {messageParamType} message, System.Threading.CancellationToken cancellationToken = default";

        source.AppendLine($"public static {returnInfo.AsyncModifier}{returnInfo.ReturnType} {interceptorMethod}({parameters})");
        source.AppendLine("{");

        source.IncrementIndent();

        source.AppendLine($"var typedMessage = ({handler.MessageType.FullName})message;");

        // Zero-alloc fast path for static handlers: call handler directly
        // Static handlers with no dependencies can be called directly (fast path)
        if (!InterceptorCodeEmitter.TryEmitStaticFastPath(source, handler, responseType, returnInfo.MethodIsAsync))
        {
            // Standard path: call HandleAsync or HandleItemN/HandleItemNAsync
            string targetMethod = InterceptorCodeEmitter.GetTargetMethodName(handler, responseType, allHandlers);
            InterceptorCodeEmitter.EmitInterceptorMethodBody(source, handler, "", targetMethod, responseType, returnInfo);
        }

        source.DecrementIndent();
        source.AppendLine("}");
    }

    /// <summary>
    /// Gets the method name for returning a specific tuple item (0-indexed).
    /// Item 0 uses HandleAsync/Handle.
    /// Items 1+ use HandleItem2Async/HandleItem2, HandleItem3Async/HandleItem3, etc.
    /// </summary>
    public static string GetHandlerItemMethodName(HandlerInfo handler, int itemIndex, bool isAsyncMethod)
    {
        if (itemIndex == 0)
            return GetHandlerMethodName(handler);

        // itemIndex 1 = Item2, itemIndex 2 = Item3, etc.
        string suffix = isAsyncMethod ? "Async" : "";
        return $"HandleItem{itemIndex + 1}{suffix}";
    }

    private static string BuildParameters(IndentedStringBuilder source, EquatableArray<ParameterInfo> parameters, Dictionary<string, string>? variables = null, string messageVar = "message")
    {
        var parameterValues = new List<string>();

        const bool outputDebugInfo = false;

        foreach (var kvp in variables ?? [])
        {
            source.AppendLineIf($"// Variable: {kvp.Key} = {kvp.Value}", outputDebugInfo);
        }

        foreach (var param in parameters)
        {
            source.AppendLineIf($"// Param: Name='{param.Name}', Type.FullName='{param.Type.FullName}', Type.QualifiedName='{param.Type.QualifiedName}', Type.UnwrappedFullName='{param.Type.UnwrappedFullName}', IsMessageParameter={param.IsMessageParameter}, Type.IsObject={param.Type.IsObject}, Type.IsCancellationToken={param.Type.IsCancellationToken}", outputDebugInfo);

            if (param.IsMessageParameter)
            {
                parameterValues.Add(messageVar);
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
            else if (variables != null && variables.TryGetValue(param.Type.QualifiedName, out string? qualifiedVariableName))
            {
                // Use QualifiedName for reliable matching regardless of using directives
                parameterValues.Add(qualifiedVariableName);
            }
            else if (variables != null && variables.TryGetValue(param.Type.FullName, out string? variableName))
            {
                parameterValues.Add(variableName);
            }
            else if (variables != null && variables.TryGetValue(param.Type.UnwrappedFullName, out string? unwrappedVariableName))
            {
                parameterValues.Add(unwrappedVariableName);
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

    private static void GenerateGetOrCreateHandler(IndentedStringBuilder source, HandlerInfo handler)
    {
        // Scoped/Transient/Singleton: Always resolve from DI inline - no wrapper method needed
        // The invocation code uses serviceProvider.GetRequiredService<T>() directly
        if (handler.RequiresDIResolutionPerInvocation ||
            string.Equals(handler.Lifetime, "Singleton", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Has constructor dependencies - must resolve from DI every time
        // We can't safely cache handlers with DI dependencies because those dependencies
        // might be scoped (e.g., IMediator, DbContext) and would become invalid after the scope ends
        if (handler.RequiresConstructorInjection)
        {
            return;
        }

        // No constructor dependencies - use new() with lazy initialization (safe to cache)
        source.AppendLine()
              .AppendLines($$"""
                private static {{handler.FullName}}? _cachedHandler;

                [DebuggerStepThrough]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static {{handler.FullName}} GetOrCreateHandler(IServiceProvider serviceProvider)
                {
                    return _cachedHandler ??= new {{handler.FullName}}();
                }
                """);
    }

    private static void GenerateMiddlewareInstantiation(IndentedStringBuilder source, HandlerInfo handler)
    {
        // Only generate for non-static middleware
        var nonStaticMiddleware = handler.Middleware.Where(m => !m.IsStatic).ToList();
        if (nonStaticMiddleware.Count == 0)
            return;

        foreach (var m in nonStaticMiddleware)
        {
            // Scoped/Transient/Singleton: Always resolve from DI - no caching method needed
            // For Singleton, the user explicitly wants DI to manage the instance
            if (m.RequiresDIResolutionPerInvocation ||
                string.Equals(m.Lifetime, "Singleton", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // No explicit lifetime (None/Default) - generate caching method
            // No constructor dependencies - use new() and cache
            if (!m.RequiresConstructorInjection)
            {
                source.AppendLine()
                      .AppendLines($$"""
                        private static {{m.FullName}}? _cached{{m.Identifier}};

                        [DebuggerStepThrough]
                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                        private static {{m.FullName}} GetOrCreate{{m.Identifier}}(IServiceProvider serviceProvider)
                        {
                            return _cached{{m.Identifier}} ??= new {{m.FullName}}();
                        }
                        """);
            }
            // Has constructor dependencies - cache the instance after first creation
            // This is safe when lifetime is None (default) because the middleware will be long-lived
            // and dependencies are resolved once. If dependencies are scoped, user should use
            // [Middleware(Lifetime = Scoped)] or MediatorDefaultMiddlewareLifetime=Scoped.
            else
            {
                source.AppendLine()
                      .AppendLines($$"""
                        private static {{m.FullName}}? _cached{{m.Identifier}};

                        [DebuggerStepThrough]
                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                        private static {{m.FullName}} GetOrCreate{{m.Identifier}}(IServiceProvider serviceProvider)
                        {
                            return _cached{{m.Identifier}} ??= ActivatorUtilities.CreateInstance<{{m.FullName}}>(serviceProvider);
                        }
                        """);
            }
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
        return handler.IsAsync || handler.ReturnType.IsTuple ? "HandleAsync" : "Handle";
    }

    /// <summary>
    /// Gets the return type name for a handler method, handling tuple returns and nullable markers.
    /// </summary>
    /// <param name="handler">The handler info.</param>
    /// <param name="tupleItemIndex">For tuple returns, the index of the item to return (0-based). Default is 0 (first item).</param>
    /// <returns>The return type name string.</returns>
    private static string GetReturnTypeName(HandlerInfo handler, int tupleItemIndex = 0)
    {
        if (handler.ReturnType.IsTuple)
        {
            return handler.ReturnType.TupleItems[tupleItemIndex].TypeFullName;
        }

        string returnTypeName = handler.ReturnType.UnwrappedFullName;
        if (handler.ReturnType.IsNullable && !returnTypeName.EndsWith("?"))
            returnTypeName += "?";
        return returnTypeName;
    }

    /// <summary>
    /// Gets the full method signature return type (including async wrappers).
    /// </summary>
    /// <param name="isAsync">Whether the method is async.</param>
    /// <param name="isVoid">Whether the return type is void.</param>
    /// <param name="returnTypeName">The inner return type name.</param>
    /// <returns>The full method return type string (e.g., "ValueTask&lt;T&gt;", "void", etc.).</returns>
    private static string GetMethodSignatureReturnType(bool isAsync, bool isVoid, string returnTypeName)
    {
        if (isAsync)
        {
            return isVoid
                ? "System.Threading.Tasks.ValueTask"
                : $"System.Threading.Tasks.ValueTask<{returnTypeName}>";
        }

        return isVoid ? "void" : returnTypeName;
    }

    /// <summary>
    /// Generates runtime DI handler calls for cascading messages based on the publish strategy.
    /// Uses Mediator.GetPublishHandlersForType for runtime handler discovery to support
    /// handlers from any assembly.
    /// </summary>
    private static void GenerateCascadingHandlerCalls(
        IndentedStringBuilder source,
        List<TupleItemInfo> publishItems,
        List<HandlerInfo> allHandlers,
        string strategy)
    {
        // Skip if no publish items
        if (publishItems.Count == 0)
            return;

        // Check if we need exception aggregation (List is only created when exception occurs)
        bool needsExceptionAggregation = strategy is "ForeachAwait" or "TaskWhenAll";

        if (needsExceptionAggregation)
        {
            source.AppendLine("System.Collections.Generic.List<System.Exception>? exceptions = null;");
        }

        switch (strategy)
        {
            case "TaskWhenAll":
                GenerateCascadingHandlerCallsTaskWhenAll(source, publishItems);
                break;
            case "FireAndForget":
                GenerateCascadingHandlerCallsFireAndForget(source, publishItems);
                break;
            case "ForeachAwait":
            default:
                GenerateCascadingHandlerCallsForeachAwait(source, publishItems);
                break;
        }

        if (needsExceptionAggregation)
        {
            source.AppendLine();
            source.AppendLine("if (exceptions != null)");
            source.AppendLine("    throw new System.AggregateException(exceptions);");
        }
    }
    private static void GenerateCascadingHandlerCallsForeachAwait(
        IndentedStringBuilder source,
        List<TupleItemInfo> publishItems)
    {
        foreach (var publishItem in publishItems)
        {
            string access = $"result.{publishItem.Name}";
            string typeFullName = publishItem.TypeFullName.TrimEnd('?');

            if (publishItem.IsNullable)
            {
                source.AppendLine($"if ({access} != null)");
                source.AppendLine("{");
                source.IncrementIndent();
            }

            // Use runtime DI lookup for handlers (no global:: prefix to handle C# keyword aliases like 'string')
            source.AppendLine($"var cascadeHandlers_{publishItem.Name} = global::Foundatio.Mediator.Mediator.GetPublishHandlersForType(mediator, typeof({typeFullName}));");
            source.AppendLine($"for (int i_{publishItem.Name} = 0; i_{publishItem.Name} < cascadeHandlers_{publishItem.Name}.Length; i_{publishItem.Name}++)");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine("try");
            source.AppendLine("{");
            source.AppendLine($"    await cascadeHandlers_{publishItem.Name}[i_{publishItem.Name}](mediator, {access}, cancellationToken).ConfigureAwait(false);");
            source.AppendLine("}");
            HandlerCodeEmitter.EmitExceptionAggregation(source);
            source.DecrementIndent();
            source.AppendLine("}");

            if (publishItem.IsNullable)
            {
                source.DecrementIndent();
                source.AppendLine("}");
            }
        }
    }

    private static void GenerateCascadingHandlerCallsTaskWhenAll(
        IndentedStringBuilder source,
        List<TupleItemInfo> publishItems)
    {
        // Collect all tasks across all publish items
        source.AppendLine("var allCascadeTasks = new System.Collections.Generic.List<System.Threading.Tasks.ValueTask>();");

        foreach (var publishItem in publishItems)
        {
            string access = $"result.{publishItem.Name}";
            string typeFullName = publishItem.TypeFullName.TrimEnd('?');

            if (publishItem.IsNullable)
            {
                source.AppendLine($"if ({access} != null)");
                source.AppendLine("{");
                source.IncrementIndent();
            }

            // Use runtime DI lookup for handlers (no global:: prefix to handle C# keyword aliases like 'string')
            source.AppendLine($"var cascadeHandlers_{publishItem.Name} = global::Foundatio.Mediator.Mediator.GetPublishHandlersForType(mediator, typeof({typeFullName}));");
            source.AppendLine($"for (int i_{publishItem.Name} = 0; i_{publishItem.Name} < cascadeHandlers_{publishItem.Name}.Length; i_{publishItem.Name}++)");
            source.AppendLine("{");
            source.AppendLine($"    allCascadeTasks.Add(cascadeHandlers_{publishItem.Name}[i_{publishItem.Name}](mediator, {access}, cancellationToken));");
            source.AppendLine("}");

            if (publishItem.IsNullable)
            {
                source.DecrementIndent();
                source.AppendLine("}");
            }
        }

        // Await all tasks with exception handling
        source.AppendLine();
        source.AppendLine("foreach (var cascadeTask in allCascadeTasks)");
        source.AppendLine("{");
        source.AppendLine("    try { await cascadeTask.ConfigureAwait(false); } catch (System.Exception ex) { exceptions ??= new System.Collections.Generic.List<System.Exception>(); exceptions.Add(ex); }");
        source.AppendLine("}");
    }

    private static void GenerateCascadingHandlerCallsFireAndForget(
        IndentedStringBuilder source,
        List<TupleItemInfo> publishItems)
    {
        foreach (var publishItem in publishItems)
        {
            string access = $"result.{publishItem.Name}";
            string typeFullName = publishItem.TypeFullName.TrimEnd('?');

            if (publishItem.IsNullable)
            {
                source.AppendLine($"if ({access} != null)");
                source.AppendLine("{");
                source.IncrementIndent();
            }

            // Use runtime DI lookup for handlers (no global:: prefix to handle C# keyword aliases like 'string')
            source.AppendLine($"var cascadeHandlers_{publishItem.Name} = global::Foundatio.Mediator.Mediator.GetPublishHandlersForType(mediator, typeof({typeFullName}));");
            source.AppendLine($"for (int i_{publishItem.Name} = 0; i_{publishItem.Name} < cascadeHandlers_{publishItem.Name}.Length; i_{publishItem.Name}++)");
            source.AppendLine("{");
            source.IncrementIndent();
            source.AppendLine($"var handler = cascadeHandlers_{publishItem.Name}[i_{publishItem.Name}];");
            source.AppendLine($"var msg = {access};");
            source.AppendLine("_ = System.Threading.Tasks.Task.Run(async () =>");
            source.AppendLine("{");
            source.AppendLine("    try");
            source.AppendLine("    {");
            source.AppendLine("        await handler(mediator, msg, System.Threading.CancellationToken.None).ConfigureAwait(false);");
            source.AppendLine("    }");
            source.AppendLine("    catch");
            source.AppendLine("    {");
            source.AppendLine("        // Swallow exceptions - fire and forget semantics");
            source.AppendLine("    }");
            source.AppendLine("}, System.Threading.CancellationToken.None);");
            source.DecrementIndent();
            source.AppendLine("}");

            if (publishItem.IsNullable)
            {
                source.DecrementIndent();
                source.AppendLine("}");
            }
        }
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
