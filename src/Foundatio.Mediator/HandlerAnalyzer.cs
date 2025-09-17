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
        if (node is not ClassDeclarationSyntax { Identifier.ValueText: var name } classDecl)
            return false;

        if (name.EndsWith("Handler") || name.EndsWith("Consumer"))
            return true;

        if (classDecl.BaseList is { Types.Count: > 0 })
        {
            foreach (var bt in classDecl.BaseList.Types)
            {
                string? typeName = bt.Type switch
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

        if (classDecl.AttributeLists.Count > 0 && classDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Name is IdentifierNameSyntax { Identifier.ValueText: "FoundatioHandler" }
                    or QualifiedNameSyntax
                    {
                        Right.Identifier.ValueText: "FoundatioHandler"
                    }))
        {
            return true;
        }

        foreach (var member in classDecl.Members)
        {
            if (member is not MethodDeclarationSyntax m || m.AttributeLists.Count <= 0)
                continue;

            if (m.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name is IdentifierNameSyntax { Identifier.ValueText: "FoundatioHandler" }
                    or QualifiedNameSyntax
                    {
                        Right.Identifier.ValueText: "FoundatioHandler"
                    }))
            {
                return true;
            }
        }

        return false;
    }

    public static List<HandlerInfo> GetHandlers(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not { } classSymbol
            || classSymbol.HasIgnoreAttribute(context.SemanticModel.Compilation))
            return [];

        // Exclude generated handler classes in Foundatio.Mediator namespace with names ending in "_Handler"
        if (classSymbol.ContainingNamespace?.ToDisplayString() == "Foundatio.Mediator" &&
            classSymbol.Name.EndsWith("_Handler"))
            return [];

        // Determine if the class should be treated as a handler class
        bool nameMatches = classSymbol.Name.EndsWith("Handler") || classSymbol.Name.EndsWith("Consumer");
        bool implementsMarker = classSymbol.AllInterfaces.Any(i => i.ToDisplayString() == "Foundatio.Mediator.IFoundatioHandler");
        bool hasClassHandlerAttribute = classSymbol.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute);

        bool treatAsHandlerClass = nameMatches || implementsMarker || hasClassHandlerAttribute;

        var handlerMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsHandlerMethod(m, context.SemanticModel.Compilation, treatAsHandlerClass))
            .ToList();

        if (handlerMethods.Count == 0)
            return [];

        var handlers = new List<HandlerInfo>();

        foreach (var handlerMethod in handlerMethods)
        {
            if (handlerMethod.Parameters.Length == 0)
                continue;

            if (handlerMethod.IsGenericMethod)
                continue; // do not support generic handler methods, only generic classes

            var messageParameter = handlerMethod.Parameters[0];
            var messageType = messageParameter.Type;

            var parameterInfos = new List<ParameterInfo>();

            foreach (var parameter in handlerMethod.Parameters)
            {
                bool isMessage = SymbolEqualityComparer.Default.Equals(parameter, messageParameter);

                parameterInfos.Add(new ParameterInfo
                {
                    Name = parameter.Name,
                    Type = TypeSymbolInfo.From(parameter.Type, context.SemanticModel.Compilation),
                    IsMessageParameter = isMessage
                });
            }

            string? messageGenericDefinition = null;
            int messageGenericArity = 0;
            if (messageType is INamedTypeSymbol namedMsg && namedMsg.IsGenericType)
            {
                messageGenericDefinition = namedMsg.ConstructUnboundGenericType().ToDisplayString();
                messageGenericArity = namedMsg.TypeArguments.Length;
            }

            var genericParamNames = classSymbol.IsGenericType
                ? classSymbol.TypeParameters.Select(tp => tp.Name).ToArray()
                : Array.Empty<string>();

            handlers.Add(new HandlerInfo
            {
                Identifier = classSymbol.Name.ToIdentifier(),
                FullName = classSymbol.ToDisplayString(),
                MethodName = handlerMethod.Name,
                MessageType = TypeSymbolInfo.From(messageType, context.SemanticModel.Compilation),
                ReturnType = TypeSymbolInfo.From(handlerMethod.ReturnType, context.SemanticModel.Compilation),
                IsStatic = handlerMethod.IsStatic,
                IsGenericHandlerClass = classSymbol.IsGenericType,
                GenericArity = classSymbol.IsGenericType ? classSymbol.TypeParameters.Length : 0,
                GenericTypeParameters = new(genericParamNames),
                MessageGenericTypeDefinitionFullName = messageGenericDefinition,
                MessageGenericArity = messageGenericArity,
                Parameters = new(parameterInfos.ToArray()),
                CallSites = [],
                Middleware = [],
            });
        }

        return handlers;
    }

    private static bool IsHandlerMethod(IMethodSymbol method, Compilation compilation, bool treatAsHandlerClass)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
            return false;

        bool hasMethodHandlerAttribute = method.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute);
        if (!treatAsHandlerClass && !hasMethodHandlerAttribute)
            return false;

        if (treatAsHandlerClass && !ValidHandlerMethodNames.Contains(method.Name))
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
