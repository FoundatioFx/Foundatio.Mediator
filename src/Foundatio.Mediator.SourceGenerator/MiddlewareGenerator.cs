using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Foundatio.Mediator;

internal static class MiddlewareGenerator
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

        // Check if the class has the FoundatioIgnore attribute
        if (HasFoundatioIgnoreAttribute(classSymbol))
            return null;

        // Find all middleware methods in this class
        var beforeMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsMiddlewareMethod(m, "Before"))
            .ToList();

        var afterMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsMiddlewareMethod(m, "After"))
            .ToList();

        var finallyMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsMiddlewareMethod(m, "Finally"))
            .ToList();

        if (beforeMethods.Count == 0 && afterMethods.Count == 0 && finallyMethods.Count == 0)
            return null;

        var middlewares = new List<MiddlewareInfo>();

        // Group methods by message type and collect type information
        var messageTypeInfos = new Dictionary<string, (bool isObjectType, bool isInterfaceType, List<string> interfaceTypes)>();
        var messageTypes = new HashSet<string>();

        foreach (var method in beforeMethods.Concat(afterMethods).Concat(finallyMethods))
        {
            if (method.Parameters.Length > 0)
            {
                var messageParam = method.Parameters[0];
                string messageTypeName = messageParam.Type.ToDisplayString();
                messageTypes.Add(messageTypeName);

                if (!messageTypeInfos.ContainsKey(messageTypeName))
                {
                    var typeSymbol = messageParam.Type;
                    bool isObjectType = typeSymbol.SpecialType == SpecialType.System_Object;
                    bool isInterfaceType = typeSymbol.TypeKind == TypeKind.Interface;
                    var interfaceTypes = new List<string>();

                    // Collect interfaces implemented by the message type
                    if (!isInterfaceType && !isObjectType)
                    {
                        interfaceTypes.AddRange(typeSymbol.AllInterfaces.Select(i => i.ToDisplayString()));
                    }

                    messageTypeInfos[messageTypeName] = (isObjectType, isInterfaceType, interfaceTypes);
                }
            }
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

            // Extract order from FoundatioOrderAttribute
            var orderAttribute = classSymbol.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "FoundatioOrderAttribute");
            int? order = null;
            if (orderAttribute != null && orderAttribute.ConstructorArguments.Length > 0)
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
                beforeMethod != null ? CreateMiddlewareMethodInfo(beforeMethod) : null,
                afterMethod != null ? CreateMiddlewareMethodInfo(afterMethod) : null,
                finallyMethod != null ? CreateMiddlewareMethodInfo(finallyMethod) : null,
                order);

            middlewares.Add(middleware);
        }

        return middlewares.Count > 0 ? middlewares : null;
    }

    private static bool IsMiddlewareMethod(IMethodSymbol method, string methodPrefix)
    {
        string[] validNames = new[] { methodPrefix, $"{methodPrefix}Async" };

        if (!validNames.Contains(method.Name) ||
            method.DeclaredAccessibility != Accessibility.Public ||
            HasFoundatioIgnoreAttribute(method))
        {
            return false;
        }

        return true;
    }

    private static MiddlewareMethodInfo CreateMiddlewareMethodInfo(IMethodSymbol method)
    {
        string returnTypeName = method.ReturnType.ToDisplayString();
        bool isAsync = method.Name.EndsWith("Async") ||
                       returnTypeName.StartsWith("Task") ||
                       returnTypeName.StartsWith("ValueTask") ||
                       returnTypeName.StartsWith("System.Threading.Tasks.Task") ||
                       returnTypeName.StartsWith("System.Threading.Tasks.ValueTask");

        var parameterInfos = new List<ParameterInfo>();

        foreach (var parameter in method.Parameters)
        {
            string parameterTypeName = parameter.Type.ToDisplayString();
            bool isMessage = SymbolEqualityComparer.Default.Equals(parameter, method.Parameters[0]); // First parameter is always the message
            bool isCancellationToken = parameterTypeName is "System.Threading.CancellationToken" or "CancellationToken";

            parameterInfos.Add(new ParameterInfo(
                parameter.Name,
                parameterTypeName,
                isMessage,
                isCancellationToken));
        }

        return new MiddlewareMethodInfo(
            method.Name,
            isAsync,
            returnTypeName,
            parameterInfos);
    }

    private static bool HasFoundatioIgnoreAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name == "FoundatioIgnoreAttribute" ||
            attr.AttributeClass?.ToDisplayString() == "Foundatio.Mediator.FoundatioIgnoreAttribute");
    }
}
