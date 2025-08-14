using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

[Generator]
public sealed class MediatorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var csharpSufficient = context.CompilationProvider
            .Select((x, _) => x is CSharpCompilation { LanguageVersion: LanguageVersion.Default or >= LanguageVersion.CSharp11 });

        var generatorConfiguration = context.AnalyzerConfigOptionsProvider
            .Combine(csharpSufficient)
            .Select((x, _) =>
            {
                var (options, isCSharpSufficient) = x;
                
                // Read DisableMediatorInterceptors property
                var interceptorsDisabled = options.GlobalOptions.TryGetValue($"build_property.{Constants.DisabledPropertyName}", out string? disableSwitch)
                    && disableSwitch.Equals("true", StringComparison.Ordinal);
                var interceptorsEnabled = !interceptorsDisabled && isCSharpSufficient;

                // Read handler lifetime property (None | Singleton | Scoped | Transient). Default: None
                var handlerLifetime = "None";
                if (options.GlobalOptions.TryGetValue($"build_property.{Constants.HandlerLifetimePropertyName}", out string? lifetime) && !string.IsNullOrWhiteSpace(lifetime))
                    handlerLifetime = lifetime.Trim();

                // Read OpenTelemetry disabled property. Default: false (OpenTelemetry enabled by default)
                var openTelemetryDisabled = options.GlobalOptions.TryGetValue($"build_property.{Constants.OpenTelemetryPropertyName}", out string? openTelemetrySwitch)
                    && openTelemetrySwitch.Equals("true", StringComparison.Ordinal);
                var openTelemetryEnabled = !openTelemetryDisabled;

                return new GeneratorConfiguration(interceptorsEnabled, handlerLifetime, openTelemetryEnabled);
            })
            .WithTrackingName(TrackingNames.Settings);

        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => CallSiteAnalyzer.IsMatch(s),
                transform: static (ctx, _) => CallSiteAnalyzer.GetCallSite(ctx))
            .Where(static cs => cs.HasValue)
            .Select(static (cs, _) => cs!.Value)
            .WithTrackingName(TrackingNames.CallSites);

        var middleware = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => MiddlewareAnalyzer.IsMatch(s),
                transform: static (ctx, _) => MiddlewareAnalyzer.GetMiddleware(ctx))
            .Where(static m => m.HasValue)
            .Select(static (middleware, _) => middleware!.Value)
            .WithTrackingName(TrackingNames.Middleware);

        var handlers = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => HandlerAnalyzer.IsMatch(s),
                transform: static (ctx, _) => HandlerAnalyzer.GetHandlers(ctx))
            .Where(static m => m is not null && m.Count > 0)
            .SelectMany(static (handlers, _) => handlers ?? [])
            .WithTrackingName(TrackingNames.Handlers);

        var compilationAndData = handlers.Collect()
            .Combine(middleware.Collect())
            .Combine(callSites.Collect())
            .Combine(generatorConfiguration)
            .Combine(context.CompilationProvider)
            .Select(static (spc, _) => (
                Handlers: spc.Left.Left.Left.Left,
                Middleware: spc.Left.Left.Left.Right,
                CallSites: spc.Left.Left.Right,
                Configuration: spc.Left.Right,
                Compilation: spc.Right
            ));

        context.RegisterImplementationSourceOutput(compilationAndData,
            static (spc, source) => Execute(source.Handlers, source.Middleware, source.CallSites, source.Configuration, source.Compilation, spc));
    }

    private static void Execute(ImmutableArray<HandlerInfo> handlers, ImmutableArray<MiddlewareInfo> middleware, ImmutableArray<CallSiteInfo> callSites, GeneratorConfiguration configuration, Compilation compilation, SourceProductionContext context)
    {
        var callSitesByMessage = callSites.ToList()
            .Where(cs => !cs.IsPublish)
            .GroupBy(cs => cs.MessageType)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var handlersWithInfo = new List<HandlerInfo>();
        foreach (var handler in handlers)
        {
            callSitesByMessage.TryGetValue(handler.MessageType, out var handlerCallSites);
            var applicableMiddleware = GetApplicableMiddlewares(middleware, handler);
            handlersWithInfo.Add(handler with { CallSites = new(handlerCallSites), Middleware = applicableMiddleware });
        }

        // Always generate diagnostics related to call sites, even if there are no handlers
        HandlerGenerator.ValidateGlobalCallSites(context, handlersWithInfo, callSites);

        if (handlersWithInfo.Count == 0)
            return;

        InterceptsLocationGenerator.Execute(context, configuration.InterceptorsEnabled);

        HandlerGenerator.Execute(context, handlersWithInfo, configuration);

        DIRegistrationGenerator.Execute(context, handlersWithInfo, compilation, configuration.HandlerLifetime);
    }

    private static EquatableArray<MiddlewareInfo> GetApplicableMiddlewares(ImmutableArray<MiddlewareInfo> middlewares, HandlerInfo handler)
    {
        var applicable = new List<MiddlewareInfo>();

        foreach (var middleware in middlewares)
        {
            if (IsMiddlewareApplicableToHandler(middleware, handler))
            {
                applicable.Add(middleware);
            }
        }

        return new EquatableArray<MiddlewareInfo>(applicable
            .OrderBy(m => m.Order)
            .ThenBy(m => m.MessageType.IsObject ? 2 : (m.MessageType.IsInterface ? 1 : 0)) // Priority: specific=0, interface=1, object=2
            .ToArray());
    }

    private static bool IsMiddlewareApplicableToHandler(MiddlewareInfo middleware, HandlerInfo handler)
    {
        if (middleware.MessageType.IsObject)
            return true;

        if (middleware.MessageType.FullName == handler.MessageType.FullName)
            return true;

        // TODO get all interfaces of handler.MessageType
        //if (middleware.MessageType.IsInterface && middleware.InterfaceTypes.Contains(handler.MessageTypeName))
        //    return true;

        return false;
    }
}

