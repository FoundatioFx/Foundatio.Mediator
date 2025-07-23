using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

[Generator]
public sealed class MediatorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var interceptionEnabledSetting = context.AnalyzerConfigOptionsProvider
            .Select((x, _) =>
                x.GlobalOptions.TryGetValue($"build_property.{Constants.EnabledPropertyName}", out string? enableSwitch)
                && !enableSwitch.Equals("false", StringComparison.Ordinal));

        var csharpSufficient = context.CompilationProvider
            .Select((x,_) => x is CSharpCompilation { LanguageVersion: LanguageVersion.Default or >= LanguageVersion.CSharp11 });

        var settings = interceptionEnabledSetting
            .Combine(csharpSufficient)
            .WithTrackingName(TrackingNames.Settings);

        var interceptionEnabled = settings
            .Select((x, _) => x is { Left: true, Right: true });

        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => CallSiteAnalyzer.IsPotentialMediatorCall(s),
                transform: static (ctx, _) => CallSiteAnalyzer.GetCallSiteForAnalysis(ctx))
            .Where(static cs => cs.HasValue)
            .Select(static (cs, _) => cs!.Value)
            .WithTrackingName(TrackingNames.CallSites);

        var middlewareDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => MiddlewareGenerator.IsPotentialMiddlewareClass(s),
                transform: static (ctx, _) => MiddlewareGenerator.GetMiddlewareForGeneration(ctx))
            .Where(static m => m is not null && m.Count > 0)
            .SelectMany(static (middlewares, _) => middlewares ?? [])
            .WithTrackingName(TrackingNames.Middleware);

        var handlerDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsPotentialHandlerClass(s),
                transform: static (ctx, _) => GetHandlersForGeneration(ctx))
            .Where(static m => m is not null && m.Count > 0)
            .SelectMany(static (handlers, _) => handlers ?? [])
            .WithTrackingName(TrackingNames.Handlers);

        var compilationAndData = context.CompilationProvider
            .Combine(handlerDeclarations.Collect())
            .Combine(middlewareDeclarations.Collect())
            .Combine(callSites.Collect())
            .Combine(interceptionEnabled);

        context.RegisterSourceOutput(compilationAndData,
            static (spc, source) => Execute(source.Left.Left.Left.Left, source.Left.Left.Left.Right, source.Left.Left.Right, source.Left.Right, source.Right, spc));
    }

    private static bool IsPotentialHandlerClass(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax { Identifier.ValueText: var name }
               && (name.EndsWith("Handler") || name.EndsWith("Consumer"));
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

        // Skip handler classes that have generic type parameters
        if (classSymbol.IsGenericType)
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

            // Skip handler methods that have generic type parameters
            if (handlerMethod.IsGenericMethod)
            {
                continue;
            }

            var messageParameter = handlerMethod.Parameters[0];
            var messageType = messageParameter.Type;
            string messageTypeName = messageType.ToDisplayString();

            // Collect message type hierarchy (exact type, interfaces, and base classes)
            var messageTypeHierarchy = new List<string>();

            // Add the exact message type
            messageTypeHierarchy.Add(messageTypeName);

            // Add all implemented interfaces
            if (messageType is INamedTypeSymbol namedMessageType)
            {
                foreach (var interfaceType in namedMessageType.AllInterfaces)
                {
                    string interfaceTypeName = interfaceType.ToDisplayString();
                    if (!messageTypeHierarchy.Contains(interfaceTypeName))
                    {
                        messageTypeHierarchy.Add(interfaceTypeName);
                    }
                }

                // Add all base classes
                var currentBaseType = namedMessageType.BaseType;
                while (currentBaseType != null && currentBaseType.SpecialType != SpecialType.System_Object)
                {
                    string baseTypeName = currentBaseType.ToDisplayString();
                    if (!messageTypeHierarchy.Contains(baseTypeName))
                    {
                        messageTypeHierarchy.Add(baseTypeName);
                    }
                    currentBaseType = currentBaseType.BaseType;
                }
            }

            string returnTypeName = handlerMethod.ReturnType.ToDisplayString();
            bool isAsync = handlerMethod.Name.EndsWith("Async") ||
                           returnTypeName.StartsWith("Task") ||
                           returnTypeName.StartsWith("ValueTask");

            // Extract the actual return type from Task<T>
            string actualReturnType = returnTypeName;
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
                string parameterTypeName = parameter.Type.ToDisplayString();
                bool isMessage = SymbolEqualityComparer.Default.Equals(parameter, messageParameter);
                bool isCancellationToken = parameterTypeName is "System.Threading.CancellationToken" or "CancellationToken";

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
                parameterInfos,
                messageTypeHierarchy));
        }

        return handlers.Count > 0 ? handlers : null;
    }

    private static bool IsHandlerMethod(IMethodSymbol method)
    {
        string[] validNames = new[] { "Handle", "Handles", "HandleAsync", "HandlesAsync",
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
        var validMiddlewares = middlewares.IsDefaultOrEmpty ? [] : middlewares.ToList();

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

        string source = MediatorImplementationGenerator.GenerateMediatorImplementation(validHandlers);
        context.AddSource("Mediator.g.cs", source);

        // Also generate DI registration
        string diSource = DIRegistrationGenerator.GenerateDIRegistration(validHandlers, validMiddlewares);
        context.AddSource("ServiceCollectionExtensions.g.cs", diSource);
    }
}
// Trigger regeneration
