using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace Foundatio.Mediator;

/// <summary>
/// Information about a discovered middleware class and its methods.
/// </summary>
public class MiddlewareInfo
{
    public MiddlewareInfo(string middlewareTypeName, string messageTypeName, bool isObjectType, bool isInterfaceType, List<string> interfaceTypes, MiddlewareMethodInfo? beforeMethod, MiddlewareMethodInfo? afterMethod, MiddlewareMethodInfo? finallyMethod, int? order = null)
    {
        MiddlewareTypeName = middlewareTypeName;
        MessageTypeName = messageTypeName;
        IsObjectType = isObjectType;
        IsInterfaceType = isInterfaceType;
        InterfaceTypes = interfaceTypes;
        BeforeMethod = beforeMethod;
        AfterMethod = afterMethod;
        FinallyMethod = finallyMethod;
        Order = order;
        IsAsync = (beforeMethod?.IsAsync == true) || (afterMethod?.IsAsync == true) || (finallyMethod?.IsAsync == true);
    }

    public string MiddlewareTypeName { get; }
    public string MessageTypeName { get; }
    public bool IsObjectType { get; }
    public bool IsInterfaceType { get; }
    public List<string> InterfaceTypes { get; }
    public MiddlewareMethodInfo? BeforeMethod { get; }
    public MiddlewareMethodInfo? AfterMethod { get; }
    public MiddlewareMethodInfo? FinallyMethod { get; }
    public int? Order { get; }
    public bool IsAsync { get; }
}

/// <summary>
/// Information about a middleware method.
/// </summary>
public class MiddlewareMethodInfo
{
    public MiddlewareMethodInfo(string methodName, bool isAsync, string returnTypeName, List<ParameterInfo> parameters)
    {
        MethodName = methodName;
        IsAsync = isAsync;
        ReturnTypeName = returnTypeName;
        Parameters = parameters;
    }

    public string MethodName { get; }
    public bool IsAsync { get; }
    public string ReturnTypeName { get; }
    public List<ParameterInfo> Parameters { get; }
}

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
                var messageTypeName = messageParam.Type.ToDisplayString();
                messageTypes.Add(messageTypeName);

                if (!messageTypeInfos.ContainsKey(messageTypeName))
                {
                    var typeSymbol = messageParam.Type;
                    var isObjectType = typeSymbol.SpecialType == SpecialType.System_Object;
                    var isInterfaceType = typeSymbol.TypeKind == TypeKind.Interface;
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

        foreach (var messageType in messageTypes)
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
        var validNames = new[] { methodPrefix, $"{methodPrefix}Async" };

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
        var returnTypeName = method.ReturnType.ToDisplayString();
        var isAsync = method.Name.EndsWith("Async") ||
                     returnTypeName.StartsWith("Task") ||
                     returnTypeName.StartsWith("ValueTask") ||
                     returnTypeName.StartsWith("System.Threading.Tasks.Task") ||
                     returnTypeName.StartsWith("System.Threading.Tasks.ValueTask");

        var parameterInfos = new List<ParameterInfo>();

        foreach (var parameter in method.Parameters)
        {
            var parameterTypeName = parameter.Type.ToDisplayString();
            var isMessage = SymbolEqualityComparer.Default.Equals(parameter, method.Parameters[0]); // First parameter is always the message
            var isCancellationToken = parameterTypeName is "System.Threading.CancellationToken" or "CancellationToken";

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
