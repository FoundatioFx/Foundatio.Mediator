namespace Foundatio.Mediator;

/// <summary>
/// Identifies whether a handler attribute was declared at the handler type or method level.
/// </summary>
public enum HandlerAttributeTarget
{
    HandlerType,
    HandlerMethod
}

/// <summary>
/// Generic metadata for an attribute discovered on a handler type or handler method.
/// This is intentionally feature-agnostic so external packages can interpret their own attributes.
/// </summary>
public sealed class HandlerAttributeMetadata
{
    private readonly Lazy<Attribute?> _lazyAttribute;
    private HandlerRegistration? _registration;

    public HandlerAttributeMetadata(
        string attributeTypeName,
        HandlerAttributeTarget target,
        IReadOnlyList<string?>? constructorArguments = null,
        IReadOnlyDictionary<string, string?>? namedArguments = null,
        string? sourceHandlerTypeName = null,
        string? sourceMethodName = null,
        IReadOnlyList<string>? sourceMethodParameterTypeNames = null)
    {
        if (string.IsNullOrWhiteSpace(attributeTypeName))
            throw new ArgumentException("Attribute type name is required.", nameof(attributeTypeName));

        AttributeTypeName = attributeTypeName;
        Target = target;
        ConstructorArguments = constructorArguments ?? Array.Empty<string?>();
        NamedArguments = namedArguments ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        SourceHandlerTypeName = sourceHandlerTypeName;
        SourceMethodName = sourceMethodName;
        SourceMethodParameterTypeNames = sourceMethodParameterTypeNames ?? Array.Empty<string>();
        _lazyAttribute = new Lazy<Attribute?>(ResolveAttribute, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// The full type name of the attribute (for example, "MyCompany.Feature.MyAttribute").
    /// </summary>
    public string AttributeTypeName { get; }

    /// <summary>
    /// Whether the attribute was declared on the handler type or method.
    /// </summary>
    public HandlerAttributeTarget Target { get; }

    /// <summary>
    /// Constructor argument values captured from source generation.
    /// Values are normalized to strings by the generator.
    /// </summary>
    public IReadOnlyList<string?> ConstructorArguments { get; }

    /// <summary>
    /// Named argument values captured from source generation.
    /// Values are normalized to strings by the generator.
    /// </summary>
    public IReadOnlyDictionary<string, string?> NamedArguments { get; }

    /// <summary>
    /// Fully qualified type name of the source handler.
    /// </summary>
    public string? SourceHandlerTypeName { get; }

    /// <summary>
    /// Source handler method name when this metadata targets a handler method.
    /// </summary>
    public string? SourceMethodName { get; }

    /// <summary>
    /// Fully qualified source handler method parameter type names in declaration order.
    /// </summary>
    public IReadOnlyList<string> SourceMethodParameterTypeNames { get; }

    /// <summary>
    /// Lazily resolves the reflected attribute instance from the source handler type or method.
    /// Returns null when source context is unavailable or the attribute cannot be resolved.
    /// </summary>
    public Attribute? Attribute => _lazyAttribute.Value;

    /// <summary>
    /// Tries to get a named argument value.
    /// </summary>
    public bool TryGetNamedArgument(string name, out string? value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            value = null;
            return false;
        }

        return NamedArguments.TryGetValue(name, out value);
    }

    internal void BindRegistration(HandlerRegistration registration)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        _registration ??= registration;
    }

    private Attribute? ResolveAttribute()
    {
        var attributeType = TypeNameResolver.Resolve(AttributeTypeName);
        if (attributeType == null || !typeof(Attribute).IsAssignableFrom(attributeType))
            return null;

        var handlerType = _registration?.SourceHandlerType ?? TypeNameResolver.Resolve(SourceHandlerTypeName);
        if (handlerType == null)
            return null;

        object[] attributes;
        if (Target == HandlerAttributeTarget.HandlerType)
        {
            attributes = handlerType.GetCustomAttributes(attributeType, inherit: true);
        }
        else
        {
            var method = _registration?.HandlerMethod ?? ResolveHandlerMethod(handlerType);
            if (method == null)
                return null;
            attributes = method.GetCustomAttributes(attributeType, inherit: true);
        }

        return attributes.FirstOrDefault() as Attribute;
    }

    private MethodInfo? ResolveHandlerMethod(Type handlerType)
    {
        if (string.IsNullOrWhiteSpace(SourceMethodName))
            return null;

        if (SourceMethodParameterTypeNames.Count > 0)
        {
            var parameterTypes = SourceMethodParameterTypeNames.Select(TypeNameResolver.Resolve).ToArray();
            if (parameterTypes.All(t => t != null))
            {
                return handlerType.GetMethod(
                    SourceMethodName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly,
                    binder: null,
                    types: parameterTypes!,
                    modifiers: null);
            }
        }

        return handlerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => string.Equals(m.Name, SourceMethodName, StringComparison.Ordinal));
    }

}

