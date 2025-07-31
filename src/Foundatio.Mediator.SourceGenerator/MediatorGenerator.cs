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
            .Select((x, _) => x is CSharpCompilation { LanguageVersion: LanguageVersion.Default or >= LanguageVersion.CSharp11 });

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
            .Combine(interceptionEnabled)
            .Select(static (spc, _) => (
                Handlers: spc.Left.Left.Left,
                Middleware: spc.Left.Left.Right,
                CallSites: spc.Left.Right,
                InterceptorsEnabled: spc.Right
            ));

        context.RegisterImplementationSourceOutput(compilationAndData,
            static (spc, source) => Execute(source.Handlers, source.Middleware, source.CallSites, source.InterceptorsEnabled, spc));
    }

    private static void Execute(ImmutableArray<HandlerInfo> handlers, ImmutableArray<MiddlewareInfo> middleware, ImmutableArray<CallSiteInfo> callSites, bool interceptorsEnabled, SourceProductionContext context)
    {
        if (handlers.IsDefaultOrEmpty)
            return;

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

        InterceptsLocationGenerator.Execute(context, interceptorsEnabled);

        HandlerGenerator.Execute(context, handlersWithInfo, interceptorsEnabled);

        DIRegistrationGenerator.Execute(context, handlersWithInfo);
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

