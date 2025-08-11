using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Foundatio.Mediator;

internal static class HandlerAnalyzer
{
    public static bool IsMatch(SyntaxNode node)
    {
        if (node is ClassDeclarationSyntax { Identifier.ValueText: var name } classDecl)
        {
            // Match by naming convention
            if (name.EndsWith("Handler") || name.EndsWith("Consumer"))
                return true;

            // Or match classes that implement the IFoundatioHandler marker interface
            // We only do a cheap syntax check here to keep predicate fast.
            // Full semantic validation happens in GetHandlers.
            if (classDecl.BaseList is { Types.Count: > 0 })
            {
                foreach (var bt in classDecl.BaseList.Types)
                {
                    // Accept unqualified or namespace-qualified names
                    var typeName = bt.Type switch
                    {
                        SimpleNameSyntax sns => sns.Identifier.ValueText,
                        QualifiedNameSyntax qns => qns.Right.Identifier.ValueText,
                        AliasQualifiedNameSyntax aq => aq.Name.Identifier.ValueText,
                        _ => (bt.Type as IdentifierNameSyntax)?.Identifier.ValueText
                    };

                    if (typeName == "IFoundatioHandler")
                        return true;
                }
            }
        }

        return false;
    }

    public static List<HandlerInfo> GetHandlers(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not { } classSymbol
            || classSymbol.HasIgnoreAttribute(context.SemanticModel.Compilation)
            || classSymbol.IsGenericType)
            return [];

        // If the class name doesn't match the convention, also allow the marker interface IFoundatioHandler
        bool nameMatches = classSymbol.Name.EndsWith("Handler") || classSymbol.Name.EndsWith("Consumer");
        if (!nameMatches)
        {
            var implementsMarker = classSymbol.AllInterfaces.Any(i => i.ToDisplayString() == "Foundatio.Mediator.IFoundatioHandler");
            if (!implementsMarker)
                return [];
        }

        var handlerMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsHandlerMethod(m, context.SemanticModel.Compilation))
            .ToList();

        if (handlerMethods.Count == 0)
            return [];

        var handlers = new List<HandlerInfo>();

        foreach (var handlerMethod in handlerMethods)
        {
            if (handlerMethod.Parameters.Length == 0)
                continue;

            if (handlerMethod.IsGenericMethod)
                continue;

            var messageParameter = handlerMethod.Parameters[0];
            var messageType = messageParameter.Type;

            var parameterInfos = new List<ParameterInfo>();

            foreach (var parameter in handlerMethod.Parameters)
            {
                string parameterTypeName = parameter.Type.ToDisplayString();
                bool isMessage = SymbolEqualityComparer.Default.Equals(parameter, messageParameter);
                bool isCancellationToken = parameter.Type.IsCancellationToken(context.SemanticModel.Compilation);

                parameterInfos.Add(new ParameterInfo
                {
                    Name = parameter.Name,
                    Type = TypeSymbolInfo.From(parameter.Type, context.SemanticModel.Compilation),
                    IsMessageParameter = isMessage
                });
            }

            handlers.Add(new HandlerInfo
            {
                Identifier = classSymbol.Name.ToIdentifier(),
                FullName = classSymbol.ToDisplayString(),
                MethodName = handlerMethod.Name,
                MessageType = TypeSymbolInfo.From(messageType, context.SemanticModel.Compilation),
                ReturnType = TypeSymbolInfo.From(handlerMethod.ReturnType, context.SemanticModel.Compilation),
                IsStatic = handlerMethod.IsStatic,
                Parameters = new(parameterInfos.ToArray()),
                CallSites = [],
                Middleware = [],
            });
        }

        return handlers;
    }

    private static bool IsHandlerMethod(IMethodSymbol method, Compilation compilation)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
            return false;

        if (!ValidHandlerMethodNames.Contains(method.Name))
            return false;

        if (method.HasIgnoreAttribute(compilation))
            return false;

        if (method.IsMassTransitConsumeMethod())
            return false;

        return true;
    }

    private static readonly string[] ValidHandlerMethodNames = [
        "Handle", "HandleAsync",
        "Handles", "HandlesAsync",
        "Consume", "ConsumeAsync",
        "Consumes", "ConsumesAsync"
    ];
}
