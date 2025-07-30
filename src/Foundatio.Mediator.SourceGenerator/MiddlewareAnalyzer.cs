using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Foundatio.Mediator;

internal static class MiddlewareAnalyzer
{
    public static bool IsPotentialMiddlewareClass(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax { Identifier.ValueText: var name }
               && name.EndsWith("Middleware");
    }

    public static List<MiddlewareInfo>? GetMiddlewareForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
            return null;

        if (classSymbol.HasIgnoreAttribute(context.SemanticModel.Compilation))
            return null;

        var beforeMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsMiddlewareBeforeMethod(m, context.SemanticModel.Compilation))
            .ToList();

        var afterMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsMiddlewareAfterMethod(m, context.SemanticModel.Compilation))
            .ToList();

        var finallyMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsMiddlewareFinallyMethod(m, context.SemanticModel.Compilation))
            .ToList();

        if (beforeMethods.Count == 0 && afterMethods.Count == 0 && finallyMethods.Count == 0)
            return null;

        var middlewares = new List<MiddlewareInfo>();

        var allMiddlewareMethods = beforeMethods.Concat(afterMethods).Concat(finallyMethods).ToArray();
        bool isStaticMiddleware = allMiddlewareMethods.All(m => m.IsStatic);

        var messageTypeInfos = new Dictionary<string, (bool isObjectType, bool isInterfaceType, List<string> interfaceTypes)>();
        var messageTypes = new HashSet<string>();

        foreach (var method in allMiddlewareMethods)
        {
            if (method.Parameters.Length <= 0)
                continue;

            var messageParam = method.Parameters[0];
            string messageTypeName = messageParam.Type.ToDisplayString();
            messageTypes.Add(messageTypeName);

            if (messageTypeInfos.ContainsKey(messageTypeName))
                continue;

            var typeSymbol = messageParam.Type;
            bool isObjectType = typeSymbol.SpecialType == SpecialType.System_Object;
            bool isInterfaceType = typeSymbol.TypeKind == TypeKind.Interface;
            var interfaceTypes = new List<string>();

            if (!isInterfaceType && !isObjectType)
            {
                interfaceTypes.AddRange(typeSymbol.AllInterfaces.Select(i => i.ToDisplayString()));
            }

            messageTypeInfos[messageTypeName] = (isObjectType, isInterfaceType, interfaceTypes);
        }

        foreach (string? messageType in messageTypes)
        {
            var beforeMethod = beforeMethods.FirstOrDefault(m =>
                m.Parameters.Length > 0 &&
                m.Parameters[0].Type.ToDisplayString() == messageType);

            var afterMethod = afterMethods.FirstOrDefault(m =>
                m.Parameters.Length > 0 &&
                m.Parameters[0].Type.ToDisplayString() == messageType);

            var finallyMethod = finallyMethods.FirstOrDefault(m =>
                m.Parameters.Length > 0 &&
                m.Parameters[0].Type.ToDisplayString() == messageType);

            var orderAttribute = classSymbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass?.Name == "FoundatioOrderAttribute");

            int? order = null;
            if (orderAttribute is { ConstructorArguments.Length: > 0 })
            {
                var orderArg = orderAttribute.ConstructorArguments[0];
                if (orderArg.Value is int orderValue)
                {
                    order = orderValue;
                }
            }

            var typeInfo = messageTypeInfos[messageType];
            var middleware = new MiddlewareInfo(
                classSymbol.ToDisplayString(),
                messageType,
                typeInfo.isObjectType,
                typeInfo.isInterfaceType,
                typeInfo.interfaceTypes,
                beforeMethod != null ? CreateMiddlewareMethodInfo(beforeMethod, context.SemanticModel.Compilation) : null,
                afterMethod != null ? CreateMiddlewareMethodInfo(afterMethod, context.SemanticModel.Compilation) : null,
                finallyMethod != null ? CreateMiddlewareMethodInfo(finallyMethod, context.SemanticModel.Compilation) : null,
                isStaticMiddleware,
                allMiddlewareMethods.Any(m => m.ReturnType.IsTask(context.SemanticModel.Compilation)),
                order);

            middlewares.Add(middleware);
        }

        return middlewares.Count > 0 ? middlewares : null;
    }

    private static bool IsMiddlewareBeforeMethod(IMethodSymbol method, Compilation compilation)
    {
        return MiddlewareBeforeMethodNames.Contains(method.Name) &&
               method.DeclaredAccessibility == Accessibility.Public &&
               !method.HasIgnoreAttribute(compilation);
    }

    private static bool IsMiddlewareAfterMethod(IMethodSymbol method, Compilation compilation)
    {
        return MiddlewareAfterMethodNames.Contains(method.Name) &&
               method.DeclaredAccessibility == Accessibility.Public &&
               !method.HasIgnoreAttribute(compilation);
    }

    private static bool IsMiddlewareFinallyMethod(IMethodSymbol method, Compilation compilation)
    {
        return MiddlewareFinallyMethodNames.Contains(method.Name) &&
               method.DeclaredAccessibility == Accessibility.Public &&
               !method.HasIgnoreAttribute(compilation);
    }
    private static MiddlewareMethodInfo CreateMiddlewareMethodInfo(IMethodSymbol method, Compilation compilation)
    {
        var returnType = method.ReturnType.IsVoid(compilation) ? null : method.ReturnType;
        bool isAsync = method.ReturnType.IsTask(compilation);
        var originalReturnType = returnType;
        returnType = isAsync && returnType != null ? method.ReturnType.UnwrapTask(compilation) : returnType;
        bool isReturnTypeNullable = returnType?.IsNullable(compilation) ?? false;
        bool isReturnTypeResult = returnType?.IsResult(compilation) ?? false;
        bool isReturnTypeHandlerResult = returnType?.IsHandlerResult(compilation) ?? false;
        bool isReturnTypeTuple = returnType is { IsTupleType: true };
        var returnTypeTupleItems = returnType is { IsTupleType: true }
            ? returnType.GetTupleItems(compilation)
            : [];

        var parameterInfos = new List<ParameterInfo>();

        foreach (var parameter in method.Parameters)
        {
            string parameterTypeName = parameter.Type.ToDisplayString();
            bool isMessage = SymbolEqualityComparer.Default.Equals(parameter, method.Parameters[0]);
            bool isCancellationToken =  parameter.Type.IsCancellationToken(compilation);
            bool isNullable = parameter.Type.IsNullable(compilation);

            parameterInfos.Add(new ParameterInfo(
                parameter.Name,
                parameterTypeName,
                isMessage,
                isCancellationToken,
                isNullable));
        }

        return new MiddlewareMethodInfo(
            method.Name,
            isAsync,
            method.IsStatic,
            originalReturnType?.ToDisplayString(),
            returnType?.ToDisplayString(),
            isReturnTypeNullable,
            isReturnTypeResult,
            isReturnTypeTuple,
            isReturnTypeHandlerResult,
            returnTypeTupleItems,
            parameterInfos);
    }

    private static readonly string[] MiddlewareBeforeMethodNames = [ "Before", "BeforeAsync" ];
    private static readonly string[] MiddlewareAfterMethodNames = [ "After", "AfterAsync" ];
    private static readonly string[] MiddlewareFinallyMethodNames = [ "Finally", "FinallyAsync" ];
}
