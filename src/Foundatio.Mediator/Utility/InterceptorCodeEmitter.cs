using Foundatio.Mediator.Models;

namespace Foundatio.Mediator.Utility;

/// <summary>
/// Shared utility for generating interceptor method code.
/// Used by both HandlerGenerator and CrossAssemblyInterceptorGenerator to eliminate duplication.
/// </summary>
internal static class InterceptorCodeEmitter
{
    /// <summary>
    /// Computes return type information for interceptor method generation.
    /// </summary>
    public static InterceptorReturnInfo ComputeReturnInfo(
        HandlerInfo handler,
        TypeSymbolInfo responseType,
        bool isAsyncMethod)
    {
        bool methodIsAsync = isAsyncMethod || handler.IsAsync || handler.ReturnType.IsTuple;

        // Determine the handler's actual return type
        string handlerReturnTypeName;
        if (handler.ReturnType.IsTuple)
        {
            int itemIndex = FindTupleItemIndex(handler, responseType);
            handlerReturnTypeName = handler.ReturnType.TupleItems[itemIndex >= 0 ? itemIndex : 0].TypeFullName;
        }
        else
        {
            handlerReturnTypeName = handler.ReturnType.UnwrappedFullName;
        }

        // Check if type conversion is needed
        bool needsTypeConversion = !responseType.IsVoid &&
            handlerReturnTypeName != responseType.UnwrappedFullName &&
            responseType.UnwrappedFullName != "object";

        bool needsObjectBoxing = !responseType.IsVoid && responseType.UnwrappedFullName == "object";

        // If we need type conversion or object boxing, we must use async/await to cast the result
        bool useAsyncAwait = (needsTypeConversion || needsObjectBoxing) && methodIsAsync && (handler.IsAsync || handler.ReturnType.IsTuple);

        string asyncModifier = useAsyncAwait ? "async " : "";

        string returnType = methodIsAsync
            ? $"System.Threading.Tasks.ValueTask<{responseType.UnwrappedFullName}>"
            : responseType.UnwrappedFullName;
        if (responseType.IsVoid)
            returnType = methodIsAsync ? "System.Threading.Tasks.ValueTask" : "void";

        return new InterceptorReturnInfo
        {
            HandlerReturnTypeName = handlerReturnTypeName,
            NeedsTypeConversion = needsTypeConversion,
            NeedsObjectBoxing = needsObjectBoxing,
            UseAsyncAwait = useAsyncAwait,
            AsyncModifier = asyncModifier,
            ReturnType = returnType,
            MethodIsAsync = methodIsAsync
        };
    }

    /// <summary>
    /// Emits the body of an interceptor method that delegates to a handler wrapper.
    /// </summary>
    /// <param name="source">The string builder to emit code to.</param>
    /// <param name="handler">The handler info.</param>
    /// <param name="wrapperClassPrefix">The wrapper class prefix (e.g., "global::Foundatio.Mediator.Generated.MyHandler.") or empty for same-class calls.</param>
    /// <param name="targetMethod">The method name to call (e.g., "HandleAsync").</param>
    /// <param name="responseType">The expected response type from the call site.</param>
    /// <param name="returnInfo">Pre-computed return type information.</param>
    public static void EmitInterceptorMethodBody(
        IndentedStringBuilder source,
        HandlerInfo handler,
        string wrapperClassPrefix,
        string targetMethod,
        TypeSymbolInfo responseType,
        InterceptorReturnInfo returnInfo)
    {
        bool handlerMethodIsAsync = handler.IsAsync || handler.ReturnType.IsTuple;
        string methodCall = string.IsNullOrEmpty(wrapperClassPrefix)
            ? $"{targetMethod}(mediator, typedMessage, cancellationToken)"
            : $"{wrapperClassPrefix}.{targetMethod}(mediator, typedMessage, cancellationToken)";

        if (responseType.IsVoid)
        {
            if (handlerMethodIsAsync)
            {
                source.AppendLine($"return {methodCall};");
            }
            else if (returnInfo.MethodIsAsync)
            {
                source.AppendLine($"{methodCall};");
                source.AppendLine("return default;");
            }
            else
            {
                source.AppendLine($"{methodCall};");
            }
        }
        else if (responseType.UnwrappedFullName == "object")
        {
            // Call site expects object - need to box the result
            if (handlerMethodIsAsync)
            {
                source.AppendLine($"return (object)await {methodCall};");
            }
            else if (returnInfo.MethodIsAsync)
            {
                source.AppendLine($"return new System.Threading.Tasks.ValueTask<object>({methodCall});");
            }
            else
            {
                source.AppendLine($"return {methodCall};");
            }
        }
        else if (returnInfo.NeedsTypeConversion)
        {
            // Handler returns a different type than expected - need to await and cast
            if (handlerMethodIsAsync)
            {
                source.AppendLine($"return ({responseType.UnwrappedFullName})await {methodCall};");
            }
            else if (returnInfo.MethodIsAsync)
            {
                source.AppendLine($"return new System.Threading.Tasks.ValueTask<{responseType.UnwrappedFullName}>(({responseType.UnwrappedFullName}){methodCall});");
            }
            else
            {
                source.AppendLine($"return ({responseType.UnwrappedFullName}){methodCall};");
            }
        }
        else
        {
            if (handlerMethodIsAsync)
            {
                source.AppendLine($"return {methodCall};");
            }
            else if (returnInfo.MethodIsAsync)
            {
                source.AppendLine($"return new System.Threading.Tasks.ValueTask<{responseType.UnwrappedFullName}>({methodCall});");
            }
            else
            {
                source.AppendLine($"return {methodCall};");
            }
        }
    }

    /// <summary>
    /// Emits the zero-allocation fast path for static handlers with no dependencies.
    /// Returns true if fast path was emitted, false otherwise.
    /// </summary>
    public static bool TryEmitStaticFastPath(
        IndentedStringBuilder source,
        HandlerInfo handler,
        TypeSymbolInfo responseType,
        bool methodIsAsync)
    {
        if (!handler.IsStaticWithNoDependencies)
            return false;

        bool hasCancellationToken = handler.Parameters.Any(p => p.Type.IsCancellationToken);
        string handlerArgs = hasCancellationToken ? "typedMessage, cancellationToken" : "typedMessage";

        if (responseType.IsVoid)
        {
            if (handler.IsAsync)
            {
                if (handler.ReturnType.IsValueTask)
                    source.AppendLine($"return {handler.FullName}.{handler.MethodName}({handlerArgs});");
                else
                    source.AppendLine($"return {handler.FullName}.{handler.MethodName}({handlerArgs}).AsValueTask();");
            }
            else
            {
                source.AppendLine($"{handler.FullName}.{handler.MethodName}({handlerArgs});");
                if (methodIsAsync)
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
                    source.AppendLine($"return {handler.FullName}.{handler.MethodName}({handlerArgs}).AsValueTaskAwaited();");
                else
                    source.AppendLine($"return {handler.FullName}.{handler.MethodName}({handlerArgs}).AsValueTask();");
            }
            else if (methodIsAsync)
            {
                source.AppendLine($"return new System.Threading.Tasks.ValueTask<{responseType.UnwrappedFullName}>({handler.FullName}.{handler.MethodName}({handlerArgs}));");
            }
            else
            {
                source.AppendLine($"return {handler.FullName}.{handler.MethodName}({handlerArgs});");
            }
        }

        return true;
    }

    /// <summary>
    /// Finds the index of the tuple item that matches the requested response type.
    /// Returns -1 if no match is found.
    /// </summary>
    public static int FindTupleItemIndex(HandlerInfo handler, TypeSymbolInfo responseType)
    {
        if (!handler.ReturnType.IsTuple)
            return -1;

        var tupleItems = handler.ReturnType.TupleItems;
        for (int i = 0; i < tupleItems.Length; i++)
        {
            // Strip nullable marker for comparison
            var itemTypeName = tupleItems[i].TypeFullName.TrimEnd('?');
            var responseTypeName = responseType.UnwrappedFullName.TrimEnd('?');

            if (itemTypeName == responseTypeName)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Gets the target method name for a handler based on the response type.
    /// For tuple handlers, returns HandleItemN/HandleItemNAsync for the matching tuple item.
    /// </summary>
    public static string GetTargetMethodName(HandlerInfo handler, TypeSymbolInfo responseType, List<HandlerInfo>? allHandlers = null)
    {
        if (!handler.ReturnType.IsTuple)
        {
            return handler.IsAsync ? "HandleAsync" : "Handle";
        }

        int itemIndex = FindTupleItemIndex(handler, responseType);
        if (itemIndex <= 0)
        {
            // Item 0 (or not found): use HandleAsync/Handle based on handler
            // The main HandleAsync method for tuples is always async because it handles cascading
            return "HandleAsync";
        }

        // For itemIndex >= 1, check if the method needs to be async
        bool isAsyncMethod = IsItemMethodAsync(handler, itemIndex, allHandlers);
        return GetHandlerItemMethodName(itemIndex, isAsyncMethod);
    }

    /// <summary>
    /// Determines if a HandleItemN method should be async.
    /// It's async if the handler is async or if there are cascading handlers for the other tuple items.
    /// </summary>
    private static bool IsItemMethodAsync(HandlerInfo handler, int targetIndex, List<HandlerInfo>? allHandlers)
    {
        if (handler.IsAsync)
            return true;

        if (allHandlers == null || !handler.ReturnType.IsTuple)
            return true; // Default to async if we can't determine

        var tupleItems = handler.ReturnType.TupleItems;

        // Get all items except the target item (those need to be cascaded)
        var itemsToCascade = tupleItems
            .Select((item, index) => (item, index))
            .Where(x => x.index != targetIndex)
            .Select(x => x.item)
            .ToList();

        // With runtime DI lookup, always assume there might be handlers for cascaded messages
        return itemsToCascade.Count > 0;
    }

    /// <summary>
    /// Gets the method name for returning a specific tuple item (0-indexed).
    /// Item 0 uses HandleAsync/Handle, Items 1+ use HandleItem2/HandleItem2Async, etc.
    /// </summary>
    public static string GetHandlerItemMethodName(int itemIndex, bool isAsyncMethod)
    {
        if (itemIndex == 0)
            return isAsyncMethod ? "HandleAsync" : "Handle";

        // itemIndex 1 = Item2, itemIndex 2 = Item3, etc.
        string suffix = isAsyncMethod ? "Async" : "";
        return $"HandleItem{itemIndex + 1}{suffix}";
    }
}

/// <summary>
/// Contains computed information about return types for interceptor generation.
/// </summary>
internal readonly record struct InterceptorReturnInfo
{
    /// <summary>
    /// The actual return type name from the handler (tuple item type or unwrapped return type).
    /// </summary>
    public string HandlerReturnTypeName { get; init; }

    /// <summary>
    /// Whether type conversion is needed between handler return type and call site expected type.
    /// </summary>
    public bool NeedsTypeConversion { get; init; }

    /// <summary>
    /// Whether the result needs to be boxed to object.
    /// </summary>
    public bool NeedsObjectBoxing { get; init; }

    /// <summary>
    /// Whether async/await is required for the interceptor method.
    /// </summary>
    public bool UseAsyncAwait { get; init; }

    /// <summary>
    /// The async modifier string ("async " or "").
    /// </summary>
    public string AsyncModifier { get; init; }

    /// <summary>
    /// The full return type string for the method signature.
    /// </summary>
    public string ReturnType { get; init; }

    /// <summary>
    /// Whether the method signature should be async (handler is async or call site is async).
    /// </summary>
    public bool MethodIsAsync { get; init; }
}
