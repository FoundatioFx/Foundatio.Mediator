using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Foundatio.Mediator;

internal static class CallSiteAnalyzer
{
    private const string MediatorInterfaceName = "IMediator";

    public static bool IsPotentialMediatorCall(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
            return false;

        // Look for member access expressions like mediator.Invoke(...), mediator.InvokeAsync(...), etc.
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var methodName = memberAccess.Name.Identifier.ValueText;
            return methodName is "Invoke" or "InvokeAsync" or "Publish" or "PublishAsync";
        }

        return false;
    }

    public static CallSiteInfo? GetCallSiteForAnalysis(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var methodName = memberAccess.Name.Identifier.ValueText;
        var isAsync = methodName.EndsWith("Async");
        var isPublish = methodName.StartsWith("Publish");

        // Get the method symbol to analyze the call
        var symbolInfo = semanticModel.GetSymbolInfo(invocation.Expression);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return null;

        // Check if this is actually a call to IMediator
        var containingType = methodSymbol.ContainingType;
        if (containingType == null || containingType.Name != MediatorInterfaceName)
            return null;

        // Get the message type from the first argument
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var firstArgument = invocation.ArgumentList.Arguments[0];
        var argumentType = semanticModel.GetTypeInfo(firstArgument.Expression);
        if (argumentType.Type == null)
            return null;

        var messageTypeName = argumentType.Type.ToDisplayString();

        // For generic methods like Invoke<TResponse>, get the response type
        string expectedResponseTypeName = "";
        if (methodSymbol.IsGenericMethod && methodSymbol.TypeArguments.Length > 0)
        {
            expectedResponseTypeName = methodSymbol.TypeArguments[0].ToDisplayString();
        }

        return new CallSiteInfo(
            methodName,
            messageTypeName,
            expectedResponseTypeName,
            isAsync,
            isPublish,
            invocation.GetLocation());
    }
}
