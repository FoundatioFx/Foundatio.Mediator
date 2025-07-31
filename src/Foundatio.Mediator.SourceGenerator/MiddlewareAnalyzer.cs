using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Foundatio.Mediator;

internal static class MiddlewareAnalyzer
{
    public static bool IsMatch(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax { Identifier.ValueText: var name }
               && name.EndsWith("Middleware");
    }

    public static MiddlewareInfo? GetMiddleware(GeneratorSyntaxContext context)
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

        // TODO: Diagnostic if multiple methods for the same lifecycle stage
        // TODO: Diagnostic if there are mixed static and instance methods
        // TODO: Diagnostic if all message types are not the same

        var beforeMethod = beforeMethods.FirstOrDefault();
        var afterMethod = afterMethods.FirstOrDefault();
        var finallyMethod = finallyMethods.FirstOrDefault();

        ITypeSymbol? messageType = beforeMethod?.Parameters[0].Type
            ?? afterMethod?.Parameters[0].Type
            ?? finallyMethod?.Parameters[0].Type;

        var isStatic = beforeMethod?.IsStatic == true && afterMethod?.IsStatic == true && finallyMethod?.IsStatic == true;

        if (messageType == null)
            return null;

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

        return new MiddlewareInfo
        {
            MiddlewareTypeName = classSymbol.ToDisplayString(),
            MessageType = TypeSymbolInfo.From(messageType, context.SemanticModel.Compilation),
            BeforeMethod = beforeMethod != null ? CreateMiddlewareMethodInfo(beforeMethod, context.SemanticModel.Compilation) : null,
            AfterMethod = afterMethod != null ? CreateMiddlewareMethodInfo(afterMethod, context.SemanticModel.Compilation) : null,
            FinallyMethod = finallyMethod != null ? CreateMiddlewareMethodInfo(finallyMethod, context.SemanticModel.Compilation) : null,
            IsStatic = isStatic,
            Order = order,
        };
    }

    private static MiddlewareMethodInfo CreateMiddlewareMethodInfo(IMethodSymbol method, Compilation compilation)
    {
        var parameterInfos = new List<ParameterInfo>();

        foreach (var parameter in method.Parameters)
        {
            bool isMessage = SymbolEqualityComparer.Default.Equals(parameter, method.Parameters[0]);

            parameterInfos.Add(new ParameterInfo
            {
                Name = parameter.Name,
                Type = TypeSymbolInfo.From(parameter.Type, compilation),
                IsMessageParameter = isMessage
            });
        }

        return new MiddlewareMethodInfo
        {
            MethodName = method.Name,
            MessageType = TypeSymbolInfo.From(method.Parameters[0].Type, compilation),
            ReturnType = TypeSymbolInfo.From(method.ReturnType, compilation),
            IsStatic = method.IsStatic,
            Parameters = new(parameterInfos.ToArray())
        };
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

    private static readonly string[] MiddlewareBeforeMethodNames = [ "Before", "BeforeAsync" ];
    private static readonly string[] MiddlewareAfterMethodNames = [ "After", "AfterAsync" ];
    private static readonly string[] MiddlewareFinallyMethodNames = [ "Finally", "FinallyAsync" ];
}
