using Foundatio.Mediator.Models;
using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Utility;

internal static class TypeExtensions
{
    internal static bool IsTask(this ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
            return false;

        var taskType = compilation.GetTypeByMetadataName(WellKnownTypes.Task);
        var taskOfTType = compilation.GetTypeByMetadataName(WellKnownTypes.TaskOfT);
        var valueTaskType = compilation.GetTypeByMetadataName(WellKnownTypes.ValueTask);
        var valueTaskOfTType = compilation.GetTypeByMetadataName(WellKnownTypes.ValueTaskOfT);

        if (SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, taskType)
            || SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, taskOfTType)
            || SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, valueTaskType)
            || SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, valueTaskOfTType))
        {
            return true;
        }

        return false;
    }

    internal static ITypeSymbol UnwrapTask(this ITypeSymbol typeSymbol, Compilation compilation)
    {
        var taskOfTType = compilation.GetTypeByMetadataName(WellKnownTypes.TaskOfT);
        var valueTaskOfTType = compilation.GetTypeByMetadataName(WellKnownTypes.ValueTaskOfT);

        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length == 1 &&
            (SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, taskOfTType) ||
             SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, valueTaskOfTType)))
        {
            return namedType.TypeArguments[0];
        }

        return typeSymbol;
    }

    internal static ITypeSymbol UnwrapNullable(this ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length == 1 &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return namedType.TypeArguments[0];
        }

        return typeSymbol;
    }

    internal static bool IsResult(this ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
            return false;

        var resultType = compilation.GetTypeByMetadataName(WellKnownTypes.Result);
        var resultOfTType = compilation.GetTypeByMetadataName(WellKnownTypes.ResultOfT);

        if (SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, resultType)
            || SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, resultOfTType))
        {
            return true;
        }

        return false;
    }

    internal static bool IsObject(this ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol is INamedTypeSymbol namedType)
        {
            var objectType = compilation.GetSpecialType(SpecialType.System_Object);
            return SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, objectType);
        }

        return false;
    }

    internal static bool IsHandlerResult(this ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
            return false;

        var handlerResultType = compilation.GetTypeByMetadataName(WellKnownTypes.HandlerResult);
        if (handlerResultType == null)
            return false;

        return SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, handlerResultType);
    }

    internal static bool IsNullable(this ITypeSymbol typeSymbol, Compilation compilation)
    {
        return typeSymbol.NullableAnnotation == NullableAnnotation.Annotated ||
               typeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    internal static bool IsCancellationToken(this ITypeSymbol typeSymbol, Compilation compilation)
    {
        var ctSymbol = compilation.GetTypeByMetadataName(WellKnownTypes.CancellationToken);
        return SymbolEqualityComparer.Default.Equals(typeSymbol, ctSymbol);
    }

    internal static bool IsMassTransitConsumeMethod(this IMethodSymbol method)
    {
        if (method.Name != "Consume" || method.Parameters.Length == 0)
            return false;

        var firstParameter = method.Parameters[0];
        var parameterType = firstParameter.Type;

        if (parameterType is INamedTypeSymbol namedType)
        {
            return namedType.Name == "ConsumeContext" &&
                   namedType.ContainingNamespace?.ToDisplayString() == "MassTransit";
        }

        return false;
    }

    internal static bool HasIgnoreAttribute(this ISymbol symbol, Compilation compilation)
    {
        var ignoreSymbol = compilation.GetTypeByMetadataName(WellKnownTypes.IgnoreAttribute);
        return symbol.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, ignoreSymbol));
    }

    internal static bool IsTuple(this ITypeSymbol typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol { IsTupleType: true };
    }

    internal static bool IsVoid(this ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
            return false;

        var voidType = compilation.GetSpecialType(SpecialType.System_Void);
        if (SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, voidType))
            return true;

        return false;
    }

    internal static EquatableArray<TupleItemInfo> GetTupleItems(this ITypeSymbol typeSymbol, Compilation compilation)
    {
        if (typeSymbol is not INamedTypeSymbol { IsTupleType: true } named)
            return EquatableArray<TupleItemInfo>.Empty;

        return new EquatableArray<TupleItemInfo>(named.TupleElements
            .Select(e => new TupleItemInfo
            {
                Name = e.Name ?? e.CorrespondingTupleField!.Name,
                Field = e.CorrespondingTupleField!.Name,
                IsNullable = e.Type.IsNullable(compilation),
                TypeFullName = e.Type.ToDisplayString()
            }).ToArray());
    }

    internal static bool IsHandlerExecutionInfo(this ITypeSymbol typeSymbol, Compilation compilation)
    {
        var handlerExecutionInfoSymbol = compilation.GetTypeByMetadataName(WellKnownTypes.HandlerExecutionInfo);
        return SymbolEqualityComparer.Default.Equals(typeSymbol, handlerExecutionInfoSymbol);
    }
}

internal static class WellKnownTypes {
    public const string ValueTask = "System.Threading.Tasks.ValueTask";
    public const string ValueTaskOfT = "System.Threading.Tasks.ValueTask`1";
    public const string Task = "System.Threading.Tasks.Task";
    public const string TaskOfT = "System.Threading.Tasks.Task`1";
    public const string Result = "Foundatio.Mediator.Result";
    public const string ResultOfT = "Foundatio.Mediator.Result`1";
    public const string HandlerResult = "Foundatio.Mediator.HandlerResult";
    public const string IgnoreAttribute = "Foundatio.Mediator.FoundatioIgnoreAttribute";
    public const string HandlerAttribute = "Foundatio.Mediator.HandlerAttribute";
    public const string MiddlewareAttribute = "Foundatio.Mediator.MiddlewareAttribute";
    public const string FoundatioModuleAttribute = "Foundatio.Mediator.FoundatioModuleAttribute";
    public const string CancellationToken = "System.Threading.CancellationToken";
    public const string HandlerExecutionInfo = "Foundatio.Mediator.HandlerExecutionInfo";
}
