using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Utility;

internal readonly record struct TypeSymbolInfo
{
    /// <summary>
    /// The name of the type, without namespace or generic parameters.
    /// </summary>
    public string Name { get; init; }
    /// <summary>
    /// The full name of the type, including namespace and any generic parameters.
    /// </summary>
    public string FullName { get; init; }
    /// <summary>
    /// The unwrapped full name of the type, which is the type without any nullable or task wrappers.
    /// </summary>
    public string UnwrappedFullName { get; init; }
    /// <summary>
    /// Indicates if the type is nullable, meaning it can be null or has a nullable reference type.
    /// </summary>
    public bool IsNullable { get; init; }
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
    /// Indicates if the type is a tuple type.
    /// </summary>
    public bool IsTuple { get; init; }
    /// <summary>
    /// Contains information about the items in a tuple type, if applicable.
    /// </summary>
    public EquatableArray<TupleItemInfo> TupleItems { get; init; }

    public static TypeSymbolInfo From(ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol == null)
            throw new ArgumentNullException(nameof(typeSymbol));

        bool isTask = typeSymbol.IsTask(compilation);
        var unwrappedType = typeSymbol.UnwrapTask(compilation);
        bool isNullable = unwrappedType.IsNullable(compilation);
        var unwrappedNullableType = unwrappedType.UnwrapNullable(compilation);

        // void or Task or ValueTask
        bool isVoid = unwrappedType.IsVoid(compilation) || unwrappedType.IsTask(compilation);

        bool isObject = unwrappedType.IsObject(compilation);
        bool isInterface = unwrappedType.TypeKind == TypeKind.Interface;
        bool isResult = unwrappedNullableType.IsResult(compilation);
        bool isHandlerResult = unwrappedNullableType.IsHandlerResult(compilation);
        bool isCancellationToken = unwrappedNullableType.IsCancellationToken(compilation);
        bool isTuple = unwrappedNullableType is INamedTypeSymbol { IsTupleType: true };
        var tupleItems = unwrappedNullableType.GetTupleItems(compilation);

        return new TypeSymbolInfo
        {
            Name = typeSymbol.Name,
            FullName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            UnwrappedFullName = unwrappedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsNullable = isNullable,
            IsResult = isResult,
            IsVoid = isVoid,
            IsTask = isTask,
            IsObject = isObject,
            IsInterface = isInterface,
            IsHandlerResult = isHandlerResult,
            IsCancellationToken = isCancellationToken,
            IsTuple = isTuple,
            TupleItems = tupleItems
        };
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
}
