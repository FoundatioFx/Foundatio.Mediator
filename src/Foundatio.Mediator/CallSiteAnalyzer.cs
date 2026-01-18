using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

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
        bool isAsyncMethod = methodName.EndsWith("Async", StringComparison.Ordinal);

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

        // Check if the method parameter type is IRequest<T> rather than object
        // This determines which overload is being called
        bool usesIRequestOverload = false;
        if (methodSymbol?.Parameters.Length > 0)
        {
            var firstParamType = methodSymbol.Parameters[0].Type;
            if (firstParamType is INamedTypeSymbol namedType &&
                namedType.IsGenericType &&
                namedType.ConstructedFrom.ToDisplayString().StartsWith("Foundatio.Mediator.IRequest<"))
            {
                usesIRequestOverload = true;
            }
        }

        // For PublishAsync, we need to know the message type's interfaces and base classes
        // to match against handlers that handle interface or base class types
        var messageInterfaces = new List<string>();
        var messageBaseClasses = new List<string>();

        if (isPublish && messageType is INamedTypeSymbol namedMessageType)
        {
            foreach (var iface in namedMessageType.AllInterfaces)
            {
                messageInterfaces.Add(iface.ToDisplayString());
            }

            var currentBase = namedMessageType.BaseType;
            while (currentBase != null && currentBase.SpecialType != SpecialType.System_Object)
            {
                messageBaseClasses.Add(currentBase.ToDisplayString());
                currentBase = currentBase.BaseType;
            }
        }

        return new CallSiteInfo
        {
            MethodName = methodName,
            MessageType = TypeSymbolInfo.From(messageType, semanticModel.Compilation),
            ResponseType = responseType is not null ? TypeSymbolInfo.From(responseType, semanticModel.Compilation) : TypeSymbolInfo.Void(),
            IsAsyncMethod = isAsyncMethod,
            IsPublish = isPublish,
            Location = LocationInfo.CreateFrom(invocation, interceptableLocation)!.Value,
            UsesIRequestOverload = usesIRequestOverload,
            MessageInterfaces = new EquatableArray<string>(messageInterfaces.ToArray()),
            MessageBaseClasses = new EquatableArray<string>(messageBaseClasses.ToArray()),
        };
    }

    private const string MediatorInterfaceName = "IMediator";
    private const string MediatorNamespace = "Foundatio.Mediator";
}
