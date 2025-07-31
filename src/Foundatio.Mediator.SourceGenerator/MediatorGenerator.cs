using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
                predicate: static (s, _) => CallSiteAnalyzer.IsMatch(s),
                transform: static (ctx, _) => CallSiteAnalyzer.GetCallSite(ctx))
            .Where(static cs => cs.HasValue)
            .Select(static (cs, _) => cs!.Value)
            .WithTrackingName(TrackingNames.CallSites);

        var middlewareDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => MiddlewareAnalyzer.IsMatch(s),
                transform: static (ctx, _) => MiddlewareAnalyzer.GetMiddleware(ctx))
            .Where(static m => m.HasValue)
            .Select(static (middleware, _) => middleware!.Value)
            .WithTrackingName(TrackingNames.Middleware);

        var handlerDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => HandlerAnalyzer.IsMatch(s),
                transform: static (ctx, _) => HandlerAnalyzer.GetHandlers(ctx))
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
            .GroupBy(cs => cs.MessageType)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var handlersWithCallSites = new List<HandlerInfo>();
        foreach (var handler in validHandlers)
        {
            callSitesByMessage.TryGetValue(handler.MessageType, out var handlerCallSites);
            handlersWithCallSites.Add(handler with { CallSites = new(handlerCallSites) });
        }

        // Validate handler configurations and emit diagnostics
        MediatorValidator.ValidateHandlerConfiguration(handlersWithCallSites, context);

        // Validate call sites against available handlers
        MediatorValidator.ValidateCallSites(handlersWithCallSites, validMiddlewares, callSites.ToList(), context);

        // Generate the InterceptsLocationAttribute if interceptors are enabled
        InterceptsLocationAttributeGenerator.GenerateInterceptsLocationAttribute(context, interceptorsEnabled);

        // Generate one wrapper file per handler (now with middleware support and call sites embedded)
        HandlerWrapperGenerator.GenerateHandlerWrappers(handlersWithCallSites, validMiddlewares, interceptorsEnabled, context);

        // Also generate DI registration
        string diSource = DIRegistrationGenerator.GenerateDIRegistration(handlersWithCallSites, validMiddlewares);
        context.AddSource("ServiceCollectionExtensions.g.cs", diSource);
    }
}

