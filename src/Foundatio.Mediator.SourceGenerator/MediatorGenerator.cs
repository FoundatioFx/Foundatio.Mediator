using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
                x.GlobalOptions.TryGetValue($"build_property.{Constants.DisabledPropertyName}", out string? disableSwitch)
                && disableSwitch.Equals("true", StringComparison.Ordinal));

        var csharpSufficient = context.CompilationProvider
            .Select((x,_) => x is CSharpCompilation { LanguageVersion: LanguageVersion.Default or >= LanguageVersion.CSharp11 });

        var settings = interceptionEnabledSetting
            .Combine(csharpSufficient)
            .WithTrackingName(TrackingNames.Settings);

        var interceptionEnabled = settings
            .Select((x, _) => x is { Left: false, Right: true });

        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => CallSiteAnalyzer.IsPotentialMediatorCall(s),
                transform: static (ctx, _) => CallSiteAnalyzer.GetCallSite(ctx))
            .Where(static cs => cs.HasValue)
            .Select(static (cs, _) => cs!.Value)
            .WithTrackingName(TrackingNames.CallSites);

        var middlewareDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => MiddlewareAnalyzer.IsPotentialMiddlewareClass(s),
                transform: static (ctx, _) => MiddlewareAnalyzer.GetMiddlewareForGeneration(ctx))
            .Where(static m => m is not null && m.Count > 0)
            .SelectMany(static (middlewares, _) => middlewares ?? [])
            .WithTrackingName(TrackingNames.Middleware);

        var handlerDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsPotentialHandlerClass(s),
                transform: static (ctx, _) => GetHandlers(ctx))
            .Where(static m => m is not null && m.Count > 0)
            .SelectMany(static (handlers, _) => handlers ?? [])
            .WithTrackingName(TrackingNames.Handlers);

        var compilationAndData = context.CompilationProvider
            .Combine(handlerDeclarations.Collect())
            .Combine(middlewareDeclarations.Collect())
            .Combine(callSites.Collect())
            .Combine(interceptionEnabled)
            .Select(static (spc, _) => (
                Compilation: spc.Left.Left.Left.Left,
                Handlers: spc.Left.Left.Left.Right,
                Middleware: spc.Left.Left.Right,
                CallSites: spc.Left.Right,
                InterceptorsEnabled: spc.Right
            ));

        context.RegisterImplementationSourceOutput(compilationAndData,
            static (spc, source) => Execute(source.Compilation, source.Handlers, source.Middleware, source.CallSites, source.InterceptorsEnabled, spc));
    }

    private static bool IsPotentialHandlerClass(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax { Identifier.ValueText: var name }
               && (name.EndsWith("Handler") || name.EndsWith("Consumer"));
    }

    private static List<HandlerInfo> GetHandlers(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not { } classSymbol
            || classSymbol.HasIgnoreAttribute(context.SemanticModel.Compilation)
            || classSymbol.IsGenericType)
            return [];

        var handlerMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsHandlerMethod(m, context.SemanticModel.Compilation))
            .ToList();

        if (handlerMethods.Count == 0)
            return [];

        var handlers = new List<HandlerInfo>();

        foreach (var handlerMethod in handlerMethods)
        {
            if (handlerMethod.Parameters.Length == 0)
                continue;

            if (handlerMethod.IsGenericMethod)
                continue;

            var messageParameter = handlerMethod.Parameters[0];
            var messageType = messageParameter.Type;
            string messageTypeName = messageType.ToDisplayString();

            var originalReturnType = handlerMethod.ReturnType;
            bool isAsync = handlerMethod.ReturnType.IsTask(context.SemanticModel.Compilation);

            var returnType = handlerMethod.ReturnType.UnwrapTask(context.SemanticModel.Compilation);
            returnType = returnType == null || returnType.IsVoid(context.SemanticModel.Compilation) ? null : returnType;

            bool isReturnTypeNullable = returnType?.IsNullable(context.SemanticModel.Compilation) ?? false;
            bool isReturnTypeResult = returnType?.IsResult(context.SemanticModel.Compilation) ?? false;
            var returnTypeTupleItems = returnType is { IsTupleType: true }
                ? returnType.GetTupleItems(context.SemanticModel.Compilation)
                : [];

            var parameterInfos = new List<ParameterInfo>();

            foreach (var parameter in handlerMethod.Parameters)
            {
                string parameterTypeName = parameter.Type.ToDisplayString();
                bool isMessage = SymbolEqualityComparer.Default.Equals(parameter, messageParameter);
                bool isCancellationToken = parameter.Type.IsCancellationToken(context.SemanticModel.Compilation);
                bool isNullable = parameter.Type.IsNullable(context.SemanticModel.Compilation);

                parameterInfos.Add(new ParameterInfo(
                    parameter.Name,
                    parameterTypeName,
                    isMessage,
                    isCancellationToken,
                    isNullable));
            }

            handlers.Add(new HandlerInfo(
                classSymbol.ToDisplayString(),
                messageTypeName,
                handlerMethod.Name,
                originalReturnType?.ToDisplayString(),
                returnType?.ToDisplayString(),
                isReturnTypeNullable,
                isReturnTypeResult,
                returnType?.IsTupleType ?? false,
                returnTypeTupleItems,
                isAsync,
                handlerMethod.IsStatic,
                parameterInfos));
        }

        return handlers;
    }

    private static bool IsHandlerMethod(IMethodSymbol method, Compilation compilation)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
            return false;

        if (!ValidHandlerMethodNames.Contains(method.Name))
            return false;

        if (method.HasIgnoreAttribute(compilation))
            return false;

        if (method.IsMassTransitConsumeMethod())
            return false;

        return true;
    }

    private static void Execute(Compilation compilation, ImmutableArray<HandlerInfo> handlers, ImmutableArray<MiddlewareInfo> middlewares, ImmutableArray<CallSiteInfo> callSites, bool interceptorsEnabled, SourceProductionContext context)
    {
        if (handlers.IsDefaultOrEmpty)
            return;

        var validHandlers = handlers.ToList();
        var validMiddlewares = middlewares.IsDefaultOrEmpty ? [] : middlewares.ToList();

        if (validHandlers.Count == 0)
            return;

        var callSitesByMessage = callSites.ToList()
            .Where(cs => !cs.IsPublish)
            .GroupBy(cs => cs.MessageTypeName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var handlersWithCallSites = new List<HandlerInfo>();
        foreach (var handler in validHandlers)
        {
            callSitesByMessage.TryGetValue(handler.MessageTypeName, out var handlerCallSites);
            handlerCallSites ??= [];

            if (handlerCallSites.Count > 0 || handler.CallSites.AsSpan().Length > 0)
            {
                var updatedHandler = new HandlerInfo(
                    handler.HandlerTypeName,
                    handler.MessageTypeName,
                    handler.MethodName,
                    handler.OriginalReturnTypeName,
                    handler.ReturnTypeName,
                    handler.ReturnTypeIsNullable,
                    handler.ReturnTypeIsResult,
                    handler.ReturnTypeIsTuple,
                    handler.ReturnTypeTupleItems.ToList(),
                    handler.IsAsync,
                    handler.IsStatic,
                    handler.Parameters.ToList(),
                    handlerCallSites);
                handlersWithCallSites.Add(updatedHandler);
            }
            else
            {
                handlersWithCallSites.Add(handler);
            }
        }

        // Validate handler configurations and emit diagnostics
        MediatorValidator.ValidateHandlerConfiguration(handlersWithCallSites, context);

        // Validate call sites against available handlers
        MediatorValidator.ValidateCallSites(handlersWithCallSites, validMiddlewares, callSites.ToList(), context);

        // Generate the InterceptsLocationAttribute if interceptors are enabled
        InterceptsLocationAttributeGenerator.GenerateInterceptsLocationAttribute(context, interceptorsEnabled);

        // Generate one wrapper file per handler (now with middleware support and call sites embedded)
        HandlerWrapperGenerator.GenerateHandlerWrappers(handlersWithCallSites, validMiddlewares, interceptorsEnabled, context);

        string source = MediatorImplementationGenerator.GenerateMediatorImplementation(handlersWithCallSites);
        context.AddSource("Mediator.g.cs", source);

        // Also generate DI registration
        string diSource = DIRegistrationGenerator.GenerateDIRegistration(handlersWithCallSites, validMiddlewares);
        context.AddSource("ServiceCollectionExtensions.g.cs", diSource);
    }

    private static readonly string[] ValidHandlerMethodNames = [
        "Handle", "HandleAsync",
        "Handles", "HandlesAsync",
        "Consume", "ConsumeAsync",
        "Consumes", "ConsumesAsync"
    ];
}

