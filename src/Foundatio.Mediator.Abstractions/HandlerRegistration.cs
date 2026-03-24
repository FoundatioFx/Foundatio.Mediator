using System.ComponentModel;

namespace Foundatio.Mediator;

/// <summary>
/// Registration information for a handler
/// </summary>
public sealed class HandlerRegistration
{
    private readonly Lazy<Type?> _lazySourceHandlerType;
    private readonly Lazy<MethodInfo?> _lazyHandlerMethod;

    /// <summary>
    /// Creates a new handler registration
    /// </summary>
    /// <param name="messageTypeName">The fully qualified type name of the message</param>
    /// <param name="handlerClassName">The fully qualified type name of the generated handler wrapper class</param>
    /// <param name="handleAsync">The delegate to handle the message asynchronously</param>
    /// <param name="handle">The delegate to handle the message synchronously (null for async-only handlers)</param>
    /// <param name="isAsync">Whether the handler supports async operations</param>
    /// <param name="order">The execution order for this handler during PublishAsync. Lower values execute first.</param>
    /// <param name="publishAsync">The delegate for publish scenarios (discards return value, avoids allocation)</param>
    /// <param name="orderBefore">Fully qualified type names of handlers that this handler must execute before.</param>
    /// <param name="orderAfter">Fully qualified type names of handlers that this handler must execute after.</param>
    /// <param name="sourceHandlerName">The short name of the original handler class (e.g., "OrderHandler"). Used for diagnostic logging.</param>
    /// <param name="methodName">The handler method name (e.g., "HandleAsync"). Used for diagnostic logging.</param>
    /// <param name="returnTypeName">The display name of the handler return type (e.g., "Result&lt;Order&gt;"). Used for diagnostic logging.</param>
    /// <param name="sourceHandlerTypeName">The fully qualified type name of the original handler class. Used by extension packages for reflection-based metadata access.</param>
    /// <param name="sourceMethodParameterTypeNames">Fully qualified source handler method parameter type names in declaration order.</param>
    /// <param name="descriptorId">Stable descriptor id for the registration. Defaults to the generated handler class name when not provided.</param>
    /// <param name="attributeMetadata">Feature-agnostic metadata for attributes discovered on the handler type/method.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public HandlerRegistration(string messageTypeName, string handlerClassName, HandleAsyncDelegate handleAsync, HandleDelegate? handle, bool isAsync, int order = int.MaxValue, PublishAsyncDelegate? publishAsync = null, string[]? orderBefore = null, string[]? orderAfter = null, string? sourceHandlerName = null, string? methodName = null, string? returnTypeName = null, string? sourceHandlerTypeName = null, IReadOnlyList<string>? sourceMethodParameterTypeNames = null, string? descriptorId = null, IReadOnlyList<HandlerAttributeMetadata>? attributeMetadata = null)
    {
        if (string.IsNullOrWhiteSpace(messageTypeName))
            throw new ArgumentException("Message type name is required.", nameof(messageTypeName));
        if (string.IsNullOrWhiteSpace(handlerClassName))
            throw new ArgumentException("Handler class name is required.", nameof(handlerClassName));
        if (handleAsync == null)
            throw new ArgumentNullException(nameof(handleAsync));

        MessageTypeName = messageTypeName;
        HandlerClassName = handlerClassName;
        HandleAsync = handleAsync;
        Handle = handle;
        IsAsync = isAsync;
        Order = order;
        OrderBefore = orderBefore ?? [];
        OrderAfter = orderAfter ?? [];
        SourceHandlerName = sourceHandlerName;
        MethodName = methodName;
        ReturnTypeName = returnTypeName;
        SourceHandlerTypeName = sourceHandlerTypeName;
        SourceMethodParameterTypeNames = sourceMethodParameterTypeNames ?? Array.Empty<string>();
        DescriptorId = !string.IsNullOrWhiteSpace(descriptorId) ? descriptorId! : handlerClassName;
        AttributeMetadata = attributeMetadata ?? Array.Empty<HandlerAttributeMetadata>();
        foreach (var attribute in AttributeMetadata)
            attribute.BindRegistration(this);
        _lazySourceHandlerType = new Lazy<Type?>(() => ResolveType(SourceHandlerTypeName), LazyThreadSafetyMode.ExecutionAndPublication);
        _lazyHandlerMethod = new Lazy<MethodInfo?>(ResolveHandlerMethod, LazyThreadSafetyMode.ExecutionAndPublication);
        // If no publish delegate provided, create a wrapper that discards the result
        PublishAsync = publishAsync ?? CreatePublishDelegate(handleAsync);
    }

    private static PublishAsyncDelegate CreatePublishDelegate(HandleAsyncDelegate handleAsync)
    {
        return (mediator, msg, cancellationToken) =>
        {
            var task = handleAsync(mediator, msg, cancellationToken, null);
            if (task.IsCompletedSuccessfully)
                return default;
            return AwaitAndDiscard(task);
        };

        static async ValueTask AwaitAndDiscard(ValueTask<object?> task)
        {
            await task.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The fully qualified type name of the message this handler processes
    /// </summary>
    public string MessageTypeName { get; }

    /// <summary>
    /// The fully qualified type name of the generated handler wrapper class
    /// </summary>
    public string HandlerClassName { get; }

    /// <summary>
    /// The delegate to handle the message
    /// </summary>
    public HandleAsyncDelegate HandleAsync { get; }

    /// <summary>
    /// The delegate to handle the message synchronously (null for async-only handlers)
    /// </summary>
    public HandleDelegate? Handle { get; }

    /// <summary>
    /// The delegate for publish scenarios (discards return value, avoids allocation when sync)
    /// </summary>
    public PublishAsyncDelegate PublishAsync { get; }

    /// <summary>
    /// Whether the handler supports async operations
    /// </summary>
    public bool IsAsync { get; }

    /// <summary>
    /// The execution order for this handler during PublishAsync.
    /// Lower values execute first. Default is int.MaxValue.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Fully qualified type names of handlers that this handler must execute before during PublishAsync.
    /// </summary>
    public IReadOnlyList<string> OrderBefore { get; }

    /// <summary>
    /// Fully qualified type names of handlers that this handler must execute after during PublishAsync.
    /// </summary>
    public IReadOnlyList<string> OrderAfter { get; }

    /// <summary>
    /// The handler method name (e.g., "HandleAsync"). Used for diagnostic logging.
    /// </summary>
    public string? MethodName { get; }

    /// <summary>
    /// The display name of the handler return type (e.g., "Result&lt;Order&gt;"). Used for diagnostic logging.
    /// </summary>
    public string? ReturnTypeName { get; }

    /// <summary>
    /// The short name of the original handler class (e.g., "OrderHandler"). Used for diagnostic logging.
    /// </summary>
    public string? SourceHandlerName { get; }

    /// <summary>
    /// Fully qualified type name of the original handler class.
    /// </summary>
    public string? SourceHandlerTypeName { get; }

    /// <summary>
    /// Fully qualified source handler method parameter type names in declaration order.
    /// </summary>
    public IReadOnlyList<string> SourceMethodParameterTypeNames { get; }

    /// <summary>
    /// Resolved handler type, loaded lazily when first requested.
    /// </summary>
    public Type? SourceHandlerType => _lazySourceHandlerType.Value;

    /// <summary>
    /// Resolved handler method, loaded lazily when first requested.
    /// </summary>
    public MethodInfo? HandlerMethod => _lazyHandlerMethod.Value;

    /// <summary>
    /// Stable descriptor id for this handler registration.
    /// Used by middleware and extension packages for metadata lookups.
    /// </summary>
    public string DescriptorId { get; }

    /// <summary>
    /// Generic attribute metadata captured for the handler type/method.
    /// </summary>
    public IReadOnlyList<HandlerAttributeMetadata> AttributeMetadata { get; }

    /// <summary>
    /// Gets all attribute metadata entries with the specified attribute type.
    /// </summary>
    public IReadOnlyList<HandlerAttributeMetadata> GetAttributes(Type attributeType)
    {
        if (attributeType == null)
            throw new ArgumentNullException(nameof(attributeType));

        var attributeTypeName = attributeType.FullName;
        if (string.IsNullOrWhiteSpace(attributeTypeName))
            throw new ArgumentException("Attribute type must have a full name.", nameof(attributeType));

        if (AttributeMetadata.Count == 0)
            return Array.Empty<HandlerAttributeMetadata>();

        return AttributeMetadata
            .Where(a => string.Equals(a.AttributeTypeName, attributeTypeName, StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>
    /// Gets all attribute metadata entries with the specified attribute type.
    /// </summary>
    public IReadOnlyList<HandlerAttributeMetadata> GetAttributes<TAttribute>() where TAttribute : Attribute
    {
        return GetAttributes(typeof(TAttribute));
    }

    /// <summary>
    /// Gets the preferred attribute entry for the specified type.
    /// Method-level attributes are preferred over type-level attributes.
    /// </summary>
    public HandlerAttributeMetadata? GetPreferredAttribute(Type attributeType)
    {
        if (attributeType == null)
            throw new ArgumentNullException(nameof(attributeType));

        var attributeTypeName = attributeType.FullName;
        if (string.IsNullOrWhiteSpace(attributeTypeName))
            throw new ArgumentException("Attribute type must have a full name.", nameof(attributeType));

        if (AttributeMetadata.Count == 0)
            return null;

        var methodAttribute = AttributeMetadata.FirstOrDefault(a =>
            string.Equals(a.AttributeTypeName, attributeTypeName, StringComparison.Ordinal) &&
            a.Target == HandlerAttributeTarget.HandlerMethod);

        if (methodAttribute != null)
            return methodAttribute;

        return AttributeMetadata.FirstOrDefault(a =>
            string.Equals(a.AttributeTypeName, attributeTypeName, StringComparison.Ordinal) &&
            a.Target == HandlerAttributeTarget.HandlerType);
    }

    /// <summary>
    /// Gets the preferred attribute entry for the specified type.
    /// Method-level attributes are preferred over type-level attributes.
    /// </summary>
    public HandlerAttributeMetadata? GetPreferredAttribute<TAttribute>() where TAttribute : Attribute
    {
        return GetPreferredAttribute(typeof(TAttribute));
    }

    private MethodInfo? ResolveHandlerMethod()
    {
        var handlerType = SourceHandlerType;
        if (handlerType == null || string.IsNullOrWhiteSpace(MethodName))
            return null;

        if (SourceMethodParameterTypeNames.Count > 0)
        {
            var parameterTypes = SourceMethodParameterTypeNames
                .Select(ResolveType)
                .ToArray();

            if (parameterTypes.All(t => t != null))
            {
                return handlerType.GetMethod(
                    MethodName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly,
                    binder: null,
                    types: parameterTypes!,
                    modifiers: null);
            }
        }

        var methods = handlerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => string.Equals(m.Name, MethodName, StringComparison.Ordinal));

        var messageType = ResolveType(MessageTypeName);
        if (messageType != null)
        {
            methods = methods.Where(m =>
            {
                var firstParam = m.GetParameters().FirstOrDefault();
                return firstParam != null && firstParam.ParameterType == messageType;
            });
        }

        return methods.FirstOrDefault();
    }

    private static Type? ResolveType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var resolvedTypeName = typeName!;

        var type = Type.GetType(resolvedTypeName, throwOnError: false);
        if (type != null)
            return type;

        type = TryResolveNestedTypeName(resolvedTypeName);
        if (type != null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(resolvedTypeName, throwOnError: false);
            if (type != null)
                return type;

            type = TryResolveNestedTypeName(resolvedTypeName, assembly);
            if (type != null)
                return type;
        }

        return null;
    }

    private static Type? TryResolveNestedTypeName(string typeName, Assembly? assembly = null)
    {
        // Roslyn display names for nested types use '.', but reflection expects '+'.
        // Try progressively replacing rightmost dots with '+' to find nested runtime names.
        var dotPositions = new List<int>();
        for (int i = 0; i < typeName.Length; i++)
        {
            if (typeName[i] == '.')
                dotPositions.Add(i);
        }

        if (dotPositions.Count == 0)
            return null;

        for (int start = dotPositions.Count - 1; start >= 0; start--)
        {
            var chars = typeName.ToCharArray();
            for (int i = start; i < dotPositions.Count; i++)
                chars[dotPositions[i]] = '+';

            var candidate = new string(chars);
            var resolved = assembly == null
                ? Type.GetType(candidate, throwOnError: false)
                : assembly.GetType(candidate, throwOnError: false);

            if (resolved != null)
                return resolved;
        }

        return null;
    }
}

/// <summary>
/// Delegate type for asynchronous handler dispatch. Used by source-generated handler wrappers.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate ValueTask<object?> HandleAsyncDelegate(IMediator mediator, object message, CancellationToken cancellationToken, Type? returnType);

/// <summary>
/// Delegate type for synchronous handler dispatch. Used by source-generated handler wrappers.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate object? HandleDelegate(IMediator mediator, object message, CancellationToken cancellationToken, Type? returnType);
