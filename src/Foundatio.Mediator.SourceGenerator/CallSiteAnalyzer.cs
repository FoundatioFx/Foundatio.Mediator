using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Foundatio.Mediator;

internal static class CallSiteAnalyzer
{
    private const string MediatorInterfaceName = "IMediator";

    public static bool IsPotentialMediatorCall(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
            return false;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        string methodName = memberAccess.Name.Identifier.ValueText;
        return methodName is "Invoke" or "InvokeAsync" or "PublishAsync";
    }

    public static CallSiteInfo? GetCallSite(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        if (context.SemanticModel.GetInterceptableLocation(invocation) is not { } interceptableLocation)
            return null;

        var symbolInfo = semanticModel.GetSymbolInfo(invocation.Expression);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return null;

        var containingType = methodSymbol.ContainingType;
        if (containingType is not { Name: MediatorInterfaceName })
            return null;

        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        string methodName = memberAccess.Name.Identifier.ValueText;
        bool isAsync = methodName.EndsWith("Async");
        bool isPublish = methodName.StartsWith("Publish");

        var firstArgument = invocation.ArgumentList.Arguments[0];
        var argumentType = semanticModel.GetTypeInfo(firstArgument.Expression);
        if (argumentType.Type == null)
            return null;

        ITypeSymbol messageType = argumentType.Type;

        ITypeSymbol? responseType = null;
        if (methodSymbol is { IsGenericMethod: true, TypeArguments.Length: 1 })
        {
            responseType = methodSymbol.TypeArguments[0];
        }

        return new CallSiteInfo(
            methodName,
            messageType.ToDisplayString(),
            messageType.IsNullable(context.SemanticModel.Compilation),
            responseType?.ToDisplayString(),
            responseType?.IsNullable(context.SemanticModel.Compilation) ?? false,
            isAsync,
            isPublish,
            LocationInfo.CreateFrom(invocation)!,
            interceptableLocation);
    }
}
