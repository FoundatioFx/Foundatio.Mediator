using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace Foundatio.Mediator;

[Generator]
public class HandlerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all handler classes and their methods
        var handlerDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsPotentialHandlerClass(s),
                transform: static (ctx, _) => GetHandlersForGeneration(ctx))
            .Where(static m => m is not null && m.Count > 0)
            .SelectMany(static (handlers, _) => handlers ?? []); // Flatten the collections

        // Find all mediator call sites
        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => CallSiteAnalyzer.IsPotentialMediatorCall(s),
                transform: static (ctx, _) => CallSiteAnalyzer.GetCallSiteForAnalysis(ctx))
            .Where(static cs => cs.HasValue)
            .Select(static (cs, _) => cs!.Value);

        // Combine handlers, call sites and generate everything
        var compilationAndData = context.CompilationProvider
            .Combine(handlerDeclarations.Collect())
            .Combine(callSites.Collect());

        context.RegisterSourceOutput(compilationAndData,
            static (spc, source) => Execute(source.Left.Left, source.Left.Right, source.Right, spc));
    }

    private static bool IsPotentialHandlerClass(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax { Identifier.ValueText: var name }
               && (name.EndsWith("Handler") || name.EndsWith("Consumer"));
    }

    private static List<HandlerToGenerate>? GetHandlersForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not { } classSymbol)
            return null;

        // Find all handler methods in this class
        var handlerMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(IsHandlerMethod)
            .ToList();

        if (handlerMethods.Count == 0)
            return null;

        var handlers = new List<HandlerToGenerate>();

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

            handlers.Add(new HandlerToGenerate(
                classSymbol.ToDisplayString(),
                messageTypeName,
                handlerMethod.Name,
                actualReturnType,
                isAsync,
                parameterInfos));
        }

        return handlers.Count > 0 ? handlers : null;
    }

    private static bool IsHandlerMethod(IMethodSymbol method)
    {
        var validNames = new[] { "Handle", "Handles", "HandleAsync", "HandlesAsync", 
                                "Consume", "Consumes", "ConsumeAsync", "ConsumesAsync" };
        
        return validNames.Contains(method.Name) && 
               method.DeclaredAccessibility == Accessibility.Public &&
               !method.IsStatic;
    }

    private static void Execute(Compilation compilation, ImmutableArray<HandlerToGenerate> handlers, ImmutableArray<CallSiteInfo> callSites, SourceProductionContext context)
    {
        if (handlers.IsDefaultOrEmpty)
            return;

        var validHandlers = handlers.ToList();
        
        if (validHandlers.Count == 0)
            return;

        // Validate handler configurations and emit diagnostics
        MediatorValidator.ValidateHandlerConfiguration(validHandlers, context);

        // Validate call sites against available handlers
        MediatorValidator.ValidateCallSites(validHandlers, callSites.ToList(), context);

        // Generate one wrapper file per handler
        HandlerWrapperGenerator.GenerateHandlerWrappers(validHandlers, context);

        var source = MediatorImplementationGenerator.GenerateMediatorImplementation(validHandlers);
        context.AddSource("Mediator.g.cs", source);
        
        // Also generate DI registration
        var diSource = DIRegistrationGenerator.GenerateDIRegistration(validHandlers);
        context.AddSource("ServiceCollectionExtensions.g.cs", diSource);
    }
}