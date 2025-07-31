using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Utility;

internal readonly record struct TypeSymbolInfo
{
    public string Name { get; init; }
    public string FullName { get; init; }
    public string UnwrappedFullName { get; init; }
    public bool IsNullable { get; init; }
    public bool IsResult { get; init; }
    public bool IsVoid { get; init; }
    public bool IsTask { get; init; }
    public bool IsObject { get; init; }
    public bool IsHandlerResult { get; init; }
    public bool IsCancellationToken { get; init; }
    public bool IsTuple { get; init; }
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
            IsHandlerResult = isHandlerResult,
            IsCancellationToken = isCancellationToken,
            IsTuple = isTuple,
            TupleItems = tupleItems
        };
    }
}

internal readonly record struct TupleItemInfo
{
    public string? Name { get; init; }
    public string Field { get; init; }
    public string FullType { get; init; }
    public bool IsNullable { get; init; }
}
