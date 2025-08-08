using Foundatio.Mediator.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Foundatio.Mediator;

internal static class CallSiteAnalyzer
{
    public static bool IsMatch(SyntaxNode node)
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

        var symbolInfo = semanticModel.GetSymbolInfo(invocation.Expression);
        IMethodSymbol? methodSymbol = symbolInfo.Symbol as IMethodSymbol;
        if (methodSymbol is null)
        {
            if (symbolInfo.CandidateSymbols is { Length: > 0 })
                methodSymbol = symbolInfo.CandidateSymbols[0] as IMethodSymbol;

            methodSymbol ??= semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        }

        var containingType = methodSymbol?.ContainingType;
        if (containingType is null || containingType.Name != MediatorInterfaceName || containingType.ContainingNamespace?.ToDisplayString() != MediatorNamespace)
            return null;

        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        var interceptableLocation = context.SemanticModel.GetInterceptableLocation(invocation);

        string methodName = memberAccess.Name.Identifier.ValueText;
        bool isPublish = methodName.Equals("PublishAsync", StringComparison.Ordinal);

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

        return new CallSiteInfo
        {
            MethodName = methodName,
            MessageType = TypeSymbolInfo.From(messageType, semanticModel.Compilation),
            ResponseType = responseType is not null ? TypeSymbolInfo.From(responseType, semanticModel.Compilation) : TypeSymbolInfo.Void(),
            IsPublish = isPublish,
            Location = LocationInfo.CreateFrom(invocation, interceptableLocation)!.Value,
        };
    }

    private const string MediatorInterfaceName = "IMediator";
    private const string MediatorNamespace = "Foundatio.Mediator";
}
