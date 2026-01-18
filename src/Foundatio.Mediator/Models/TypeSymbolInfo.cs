using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator.Models;

internal readonly record struct TypeSymbolInfo
{
    /// <summary>
    /// The short identifier of the type based on just the name and any characters that are invalid for identifiers replaced with underscores.
    /// </summary>
    public string Identifier { get; init; }
    /// <summary>
    /// The full name of the type, including namespace and any generic parameters.
    /// This may use short names when types are in scope via using directives.
    /// </summary>
    public string FullName { get; init; }
    /// <summary>
    /// The fully qualified metadata name of the type, always including the full namespace.
    /// Used for reliable type matching in variable lookups.
    /// </summary>
    public string QualifiedName { get; init; }
    /// <summary>
    /// The unwrapped full name of the type, which is the type without any nullable or task wrappers.
    /// </summary>
    public string UnwrappedFullName { get; init; }
    /// <summary>
    /// Indicates if the type is nullable, meaning it can be null or has a nullable reference type.
    /// </summary>
    public bool IsNullable { get; init; }
    /// <summary>
    /// Indicates if the type is a reference type, which means it is not a value type and can be null.
    /// </summary>
    public bool IsReferenceType { get; init; }
    /// <summary>
    /// Indicates if the type is a Result type, which is a wrapper around a value that can be null or an error.
    /// </summary>
    public bool IsResult { get; init; }
    /// <summary>
    /// Indicates if the type is void or Task or ValueTask.
    /// </summary>
    public bool IsVoid { get; init; }
    /// <summary>
    /// Indicates if the type is any form of Task and should be async.
    /// </summary>
    public bool IsTask { get; init; }
    /// <summary>
    /// Indicates if the type is a ValueTask or ValueTask&lt;T&gt; (vs Task or Task&lt;T&gt;).
    /// </summary>
    public bool IsValueTask { get; init; }
    /// <summary>
    /// Indicates if the type is System.Object.
    /// </summary>
    public bool IsObject { get; init; }
    /// <summary>
    /// Indicates if the type is an interface.
    /// </summary>
    public bool IsInterface { get; init; }
    /// <summary>
    /// Indicates if the type is a HandlerResult, which is a specific type used in middleware Before methods.
    /// </summary>
    public bool IsHandlerResult { get; init; }
    /// <summary>
    /// Indicates if the type is a CancellationToken, which is used for cancellation in async operations.
    /// </summary>
    public bool IsCancellationToken { get; init; }
    /// <summary>
    /// Indicates if the type is a HandlerExecutionInfo, which contains metadata about the executing handler.
    /// </summary>
    public bool IsHandlerExecutionInfo { get; init; }
    /// <summary>
    /// Indicates if the type is a tuple type.
    /// </summary>
    public bool IsTuple { get; init; }
    /// <summary>
    /// Indicates if the type is a generic type parameter (e.g., 'T').
    /// </summary>
    public bool IsTypeParameter { get; init; }
    /// <summary>
    /// Indicates if the type is a constructed generic type.
    /// </summary>
    public bool IsGeneric { get; init; }
    /// <summary>
    /// Contains information about the items in a tuple type, if applicable.
    /// </summary>
    public EquatableArray<TupleItemInfo> TupleItems { get; init; }

    public static TypeSymbolInfo Void()
    {
        return new TypeSymbolInfo
        {
            Identifier = "void",
            FullName = "void",
            QualifiedName = "void",
            UnwrappedFullName = "void",
            IsNullable = false,
            IsReferenceType = false,
            IsResult = false,
            IsVoid = true,
            IsTask = false,
            IsValueTask = false,
            IsObject = false,
            IsInterface = false,
            IsHandlerResult = false,
            IsCancellationToken = false,
            IsHandlerExecutionInfo = false,
            IsTuple = false,
            IsTypeParameter = false,
            TupleItems = EquatableArray<TupleItemInfo>.Empty,
            IsGeneric = false
        };
    }

    public static TypeSymbolInfo From(ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol == null)
            throw new ArgumentNullException(nameof(typeSymbol));

        bool isTask = typeSymbol.IsTask(compilation);
        var unwrappedType = typeSymbol.UnwrapTask(compilation);
        var unwrappedTypeFullName = unwrappedType.ToDisplayString();
        bool isNullable = unwrappedType.IsNullable(compilation);
        if (isNullable && unwrappedTypeFullName.EndsWith("?"))
            unwrappedTypeFullName = unwrappedTypeFullName.Substring(0, unwrappedTypeFullName.Length - 1);
        bool isReferenceType = unwrappedType.IsReferenceType;
        var unwrappedNullableType = unwrappedType.UnwrapNullable(compilation);

        // void or Task or ValueTask
        bool isVoid = unwrappedType.IsVoid(compilation) || unwrappedType.IsTask(compilation);

        bool isObject = unwrappedType.IsObject(compilation);
        bool isInterface = unwrappedType.TypeKind == TypeKind.Interface;
        bool isResult = unwrappedNullableType.IsResult(compilation);
        bool isHandlerResult = unwrappedNullableType.IsHandlerResult(compilation);
        bool isCancellationToken = unwrappedNullableType.IsCancellationToken(compilation);
        bool isHandlerExecutionInfo = unwrappedNullableType.IsHandlerExecutionInfo(compilation);
        bool isTuple = unwrappedNullableType is INamedTypeSymbol { IsTupleType: true };
        var tupleItems = unwrappedNullableType.GetTupleItems(compilation);
        bool isTypeParameter = typeSymbol.TypeKind == TypeKind.TypeParameter;

        string identifier;
        if (typeSymbol is INamedTypeSymbol named && named.IsGenericType && !named.IsUnboundGenericType)
        {
            static string GetTypeArgIdentifier(ITypeSymbol ts)
            {
                if (ts is INamedTypeSymbol nts && nts.IsGenericType && !nts.IsUnboundGenericType)
                {
                    var inner = string.Join("_", nts.TypeArguments.Select(GetTypeArgIdentifier));
                    return ($"{nts.Name.ToIdentifier()}_{inner}");
                }
                return ts.Name.ToIdentifier();
            }

            var typeArgs = string.Join("_", named.TypeArguments.Select(GetTypeArgIdentifier));
            identifier = ($"{named.Name.ToIdentifier()}_{typeArgs}");
        }
        else
        {
            identifier = typeSymbol.Name.ToIdentifier();
        }

        bool isGeneric = typeSymbol is INamedTypeSymbol { IsGenericType: true } nts && !nts.IsUnboundGenericType;
        bool isValueTask = typeSymbol.IsValueTask(compilation);

        // Get the fully qualified metadata name for reliable type matching
        var qualifiedName = GetQualifiedName(unwrappedNullableType);

        return new TypeSymbolInfo
        {
            Identifier = identifier,
            FullName = typeSymbol.ToDisplayString(),
            QualifiedName = qualifiedName,
            UnwrappedFullName = unwrappedTypeFullName,
            IsNullable = isNullable,
            IsReferenceType = isReferenceType,
            IsResult = isResult,
            IsVoid = isVoid,
            IsTask = isTask,
            IsValueTask = isValueTask,
            IsObject = isObject,
            IsInterface = isInterface,
            IsHandlerResult = isHandlerResult,
            IsCancellationToken = isCancellationToken,
            IsHandlerExecutionInfo = isHandlerExecutionInfo,
            IsTuple = isTuple,
            IsTypeParameter = isTypeParameter,
            TupleItems = tupleItems,
            IsGeneric = isGeneric
        };
    }

    /// <summary>
    /// Gets the fully qualified metadata name for a type symbol.
    /// This always includes the full namespace regardless of using directives.
    /// </summary>
    private static string GetQualifiedName(ITypeSymbol typeSymbol)
    {
        // For special types and primitives, use the metadata name
        if (typeSymbol.SpecialType != SpecialType.None)
        {
            return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).TrimStart("global::".ToCharArray());
        }

        // Build the fully qualified name by walking up the containing namespace chain
        // This ensures we always get the full namespace regardless of using directives
        var nameParts = new List<string>();

        // Handle generic types - include type arguments
        if (typeSymbol is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 0 } namedType)
        {
            var typeArgs = string.Join(", ", namedType.TypeArguments.Select(GetQualifiedName));
            nameParts.Add($"{namedType.Name}<{typeArgs}>");
        }
        else
        {
            nameParts.Add(typeSymbol.Name);
        }

        // Handle nested types
        var containingType = typeSymbol.ContainingType;
        while (containingType != null)
        {
            nameParts.Insert(0, containingType.Name);
            containingType = containingType.ContainingType;
        }

        // Add namespace parts
        var ns = typeSymbol.ContainingType?.ContainingNamespace ?? typeSymbol.ContainingNamespace;
        while (ns != null && !ns.IsGlobalNamespace)
        {
            nameParts.Insert(0, ns.Name);
            ns = ns.ContainingNamespace;
        }

        return string.Join(".", nameParts);
    }
}

internal readonly record struct TupleItemInfo
{
    /// <summary>
    /// The name of the tuple item or the name of the corresponding field if the item is not named.
    /// </summary>
    public string Name { get; init; }
    /// <summary>
    /// The name of the field corresponding to the tuple item, which is used for serialization or reflection.
    /// </summary>
    public string Field { get; init; }
    /// <summary>
    /// The full name of the type of the tuple item, which includes namespace and any generic parameters.
    /// </summary>
    public string TypeFullName { get; init; }
    /// <summary>
    /// Indicates if the tuple item type is nullable, meaning it can be null or has a nullable reference type.
    /// </summary>
    public bool IsNullable { get; init; }
    /// <summary>
    /// The full names of interfaces implemented by the tuple item type.
    /// Used for finding handlers that handle interface types.
    /// </summary>
    public EquatableArray<string> Interfaces { get; init; }
    /// <summary>
    /// The full names of base classes of the tuple item type.
    /// Used for finding handlers that handle base class types.
    /// </summary>
    public EquatableArray<string> BaseClasses { get; init; }
}
