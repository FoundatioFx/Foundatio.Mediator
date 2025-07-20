using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace Foundatio.Mediator;

[Generator]
public sealed class HandlerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Check if interceptors are enabled
        var interceptorsEnabled = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => IsInterceptorsEnabled(provider));

        // Find all handler classes and their methods
        var handlerDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsPotentialHandlerClass(s),
                transform: static (ctx, _) => GetHandlersForGeneration(ctx))
            .Where(static m => m is not null && m.Count > 0)
            .SelectMany(static (handlers, _) => handlers ?? []); // Flatten the collections

        // Find all middleware classes and their methods
        var middlewareDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => MiddlewareGenerator.IsPotentialMiddlewareClass(s),
                transform: static (ctx, _) => MiddlewareGenerator.GetMiddlewareForGeneration(ctx))
            .Where(static m => m is not null && m.Count > 0)
            .SelectMany(static (middlewares, _) => middlewares ?? []); // Flatten the collections

        // Find all mediator call sites
        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => CallSiteAnalyzer.IsPotentialMediatorCall(s),
                transform: static (ctx, _) => CallSiteAnalyzer.GetCallSiteForAnalysis(ctx))
            .Where(static cs => cs.HasValue)
            .Select(static (cs, _) => cs!.Value);

        // Combine handlers, middleware, call sites, interceptor availability and generate everything
        var compilationAndData = context.CompilationProvider
            .Combine(handlerDeclarations.Collect())
            .Combine(middlewareDeclarations.Collect())
            .Combine(callSites.Collect())
            .Combine(interceptorsEnabled);

        context.RegisterSourceOutput(compilationAndData,
            static (spc, source) => Execute(source.Left.Left.Left.Left, source.Left.Left.Left.Right, source.Left.Left.Right, source.Left.Right, source.Right, spc));
    }

    private static bool IsPotentialHandlerClass(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax { Identifier.ValueText: var name }
               && (name.EndsWith("Handler") || name.EndsWith("Consumer"));
    }

    private static bool IsInterceptorsEnabled(AnalyzerConfigOptionsProvider provider)
    {
        // First, check for explicit InterceptorsNamespaces property (preferred approach)
        var propertyNames = new[]
        {
            "build_property.InterceptorsNamespaces",
            "build_property.InterceptorsPreviewNamespaces"
        };

        foreach (var propertyName in propertyNames)
        {
            if (provider.GlobalOptions.TryGetValue(propertyName, out var value) &&
                !string.IsNullOrEmpty(value))
            {
                return true;
            }
        }

        // For .NET 9+, interceptors are stable - enable if explicitly configured or targeting net9.0+
        if (provider.GlobalOptions.TryGetValue("build_property.TargetFramework", out var targetFramework))
        {
            // Enable for .NET 9+ as interceptors are stable there
            if (targetFramework?.StartsWith("net9") == true ||
                targetFramework?.StartsWith("net1") == true) // net10+
            {
                return true;
            }
        }

        return false;
    }

    private static List<HandlerInfo>? GetHandlersForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not { } classSymbol)
            return null;

        // Check if the class has the FoundatioIgnore attribute
        if (HasFoundatioIgnoreAttribute(classSymbol))
            return null;

        // Find all handler methods in this class
        var handlerMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(IsHandlerMethod)
            .ToList();

        if (handlerMethods.Count == 0)
            return null;

        var handlers = new List<HandlerInfo>();

        // Generate a HandlerToGenerate for each handler method
        foreach (var handlerMethod in handlerMethods)
        {
            if (handlerMethod.Parameters.Length == 0)
                continue;

            var messageParameter = handlerMethod.Parameters[0];
            var messageTypeName = messageParameter.Type.ToDisplayString();
            var returnTypeName = handlerMethod.ReturnType.ToDisplayString();
            var isAsync = handlerMethod.Name.EndsWith("Async") ||
                         returnTypeName.StartsWith("Task") ||
                         returnTypeName.StartsWith("ValueTask");

            // Extract the actual return type from Task<T>
            var actualReturnType = returnTypeName;
            if (returnTypeName.StartsWith("System.Threading.Tasks.Task<"))
            {
                actualReturnType = returnTypeName.Substring("System.Threading.Tasks.Task<".Length).TrimEnd('>');
            }
            else if (returnTypeName.StartsWith("Task<"))
            {
                actualReturnType = returnTypeName.Substring("Task<".Length).TrimEnd('>');
            }

            var parameterInfos = new List<ParameterInfo>();

            foreach (var parameter in handlerMethod.Parameters)
            {
                var parameterTypeName = parameter.Type.ToDisplayString();
                var isMessage = SymbolEqualityComparer.Default.Equals(parameter, messageParameter);
                var isCancellationToken = parameterTypeName is "System.Threading.CancellationToken" or "CancellationToken";

                parameterInfos.Add(new ParameterInfo(
                    parameter.Name,
                    parameterTypeName,
                    isMessage,
                    isCancellationToken));
            }

            handlers.Add(new HandlerInfo(
                classSymbol.ToDisplayString(),
                messageTypeName,
                handlerMethod.Name,
                actualReturnType,
                returnTypeName, // Store the original return type
                isAsync,
                handlerMethod.IsStatic,
                parameterInfos));
        }

        return handlers.Count > 0 ? handlers : null;
    }

    private static bool IsHandlerMethod(IMethodSymbol method)
    {
        var validNames = new[] { "Handle", "Handles", "HandleAsync", "HandlesAsync",
                                "Consume", "Consumes", "ConsumeAsync", "ConsumesAsync" };

        if (!validNames.Contains(method.Name) ||
            method.DeclaredAccessibility != Accessibility.Public ||
            HasFoundatioIgnoreAttribute(method))
        {
            return false;
        }

        // Exclude MassTransit consume methods
        if (IsMassTransitConsumeMethod(method))
        {
            return false;
        }

        return true;
    }

    private static bool IsMassTransitConsumeMethod(IMethodSymbol method)
    {
        // Check if method name is "Consume" and first parameter is MassTransit.ConsumeContext<T>
        if (method.Name != "Consume" || method.Parameters.Length == 0)
        {
            return false;
        }

        var firstParameter = method.Parameters[0];
        var parameterType = firstParameter.Type;

        // Check if the parameter type is ConsumeContext<T> from MassTransit namespace
        if (parameterType is INamedTypeSymbol namedType)
        {
            return namedType.Name == "ConsumeContext" &&
                   namedType.ContainingNamespace?.ToDisplayString() == "MassTransit";
        }

        return false;
    }

    private static bool HasFoundatioIgnoreAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name == "FoundatioIgnoreAttribute" ||
            attr.AttributeClass?.ToDisplayString() == "Foundatio.Mediator.FoundatioIgnoreAttribute");
    }

    private static void Execute(Compilation compilation, ImmutableArray<HandlerInfo> handlers, ImmutableArray<MiddlewareInfo> middlewares, ImmutableArray<CallSiteInfo> callSites, bool interceptorsEnabled, SourceProductionContext context)
    {
        if (handlers.IsDefaultOrEmpty)
            return;

        var validHandlers = handlers.ToList();
        var validMiddlewares = middlewares.IsDefaultOrEmpty ? new List<MiddlewareInfo>() : middlewares.ToList();

        if (validHandlers.Count == 0)
            return;

        // Validate handler configurations and emit diagnostics
        MediatorValidator.ValidateHandlerConfiguration(validHandlers, context);

        // Validate call sites against available handlers
        MediatorValidator.ValidateCallSites(validHandlers, validMiddlewares, callSites.ToList(), context);

        // Generate the InterceptsLocationAttribute if interceptors are enabled
        InterceptsLocationAttributeGenerator.GenerateInterceptsLocationAttribute(context, interceptorsEnabled);

        // Generate one wrapper file per handler (now with middleware support)
        HandlerWrapperGenerator.GenerateHandlerWrappers(validHandlers, validMiddlewares, callSites.ToList(), interceptorsEnabled, context);

        var source = MediatorImplementationGenerator.GenerateMediatorImplementation(validHandlers);
        context.AddSource("Mediator.g.cs", source);

        // Also generate DI registration
        var diSource = DIRegistrationGenerator.GenerateDIRegistration(validHandlers, validMiddlewares);
        context.AddSource("ServiceCollectionExtensions.g.cs", diSource);
    }
}
// Trigger regeneration
