using System.Diagnostics;
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

                // Read MediatorDisableInterceptors property
                var interceptorsDisabled = options.GlobalOptions.TryGetValue($"build_property.{Constants.DisableInterceptorsPropertyName}", out string? disableSwitch)
                    && disableSwitch.Equals("true", StringComparison.OrdinalIgnoreCase);
                var interceptorsEnabled = !interceptorsDisabled && isCSharpSufficient;

                // Read handler lifetime property (None | Singleton | Scoped | Transient). Default: None
                var handlerLifetime = "None";
                if (options.GlobalOptions.TryGetValue($"build_property.{Constants.HandlerLifetimePropertyName}", out string? lifetime) && !string.IsNullOrWhiteSpace(lifetime))
                    handlerLifetime = lifetime.Trim();

                // Read OpenTelemetry disabled property. Default: false (OpenTelemetry enabled by default)
                var openTelemetryDisabled = options.GlobalOptions.TryGetValue($"build_property.{Constants.DisableOpenTelemetryPropertyName}", out string? openTelemetrySwitch)
                    && openTelemetrySwitch.Equals("true", StringComparison.OrdinalIgnoreCase);
                var openTelemetryEnabled = !openTelemetryDisabled;

                // Read conventional discovery disabled property. Default: false (conventional discovery enabled by default)
                var conventionalDiscoveryDisabled = options.GlobalOptions.TryGetValue($"build_property.{Constants.DisableConventionalDiscoveryPropertyName}", out string? conventionalDiscoverySwitch)
                    && conventionalDiscoverySwitch.Equals("true", StringComparison.OrdinalIgnoreCase);

                // Read generation counter enabled property. Default: false (disabled by default)
                var generationCounterEnabled = options.GlobalOptions.TryGetValue($"build_property.{Constants.EnableGenerationCounterPropertyName}", out string? counterSwitch)
                    && counterSwitch.Equals("true", StringComparison.OrdinalIgnoreCase);

                return new GeneratorConfiguration(interceptorsEnabled, handlerLifetime, openTelemetryEnabled, conventionalDiscoveryDisabled, generationCounterEnabled);
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
        var sw = Stopwatch.StartNew();

        // Filter out conventionally-discovered handlers when conventional discovery is disabled
        var filteredHandlers = configuration.ConventionalDiscoveryDisabled
            ? handlers.Where(h => h.IsExplicitlyDeclared).ToImmutableArray()
            : handlers;

        // Filter out conventionally-discovered middleware when conventional discovery is disabled
        var filteredMiddleware = configuration.ConventionalDiscoveryDisabled
            ? middleware.Where(m => m.IsExplicitlyDeclared).ToImmutableArray()
            : middleware;

        // Scan referenced assemblies for cross-assembly middleware
        var metadataMiddleware = MetadataMiddlewareScanner.ScanReferencedAssemblies(compilation);

        // Combine syntax-based middleware (from current assembly) with metadata-based middleware (from referenced assemblies)
        var allMiddleware = filteredMiddleware.ToList();
        allMiddleware.AddRange(metadataMiddleware);

        // Scan referenced assemblies for cross-assembly handlers
        var crossAssemblyHandlers = CrossAssemblyHandlerScanner.ScanReferencedAssemblies(compilation);

        var callSitesByMessage = callSites.ToList()
            .Where(cs => !cs.IsPublish)
            .GroupBy(cs => cs.MessageType)
            .ToDictionary(g => g.Key, g => g.ToArray());

        // Track which call sites are handled by cross-assembly handlers
        var crossAssemblyCallSites = new List<CallSiteInfo>();
        var crossAssemblyHandlerMessageTypes = new HashSet<string>(crossAssemblyHandlers.Select(h => h.MessageType.FullName));

        var handlersWithInfo = new List<HandlerInfo>();
        foreach (var handler in filteredHandlers)
        {
            callSitesByMessage.TryGetValue(handler.MessageType, out var handlerCallSites);
            var applicableMiddleware = GetApplicableMiddlewares(allMiddleware.ToImmutableArray(), handler, compilation);
            handlersWithInfo.Add(handler with { CallSites = new(handlerCallSites), Middleware = applicableMiddleware });
        }

        // Collect call sites that need cross-assembly interceptors
        foreach (var callSite in callSites)
        {
            if (callSite.IsPublish)
                continue;

            // Check if this message type has a handler in a referenced assembly but NOT in the current assembly
            bool hasLocalHandler = filteredHandlers.Any(h => h.MessageType.FullName == callSite.MessageType.FullName);
            bool hasCrossAssemblyHandler = crossAssemblyHandlerMessageTypes.Contains(callSite.MessageType.FullName);

            if (!hasLocalHandler && hasCrossAssemblyHandler)
            {
                crossAssemblyCallSites.Add(callSite);
            }
        }

        // Always generate diagnostics related to call sites, including cross-assembly handler validation
        HandlerGenerator.ValidateGlobalCallSites(context, handlersWithInfo, callSites, crossAssemblyHandlers);

        // Generate assembly attribute and handlers registration if there are handlers or middleware (enables cross-assembly discovery)
        if (handlersWithInfo.Count > 0 || middleware.Length > 0)
        {
            FoundatioModuleGenerator.Execute(context, compilation, handlersWithInfo, configuration);
        }

        // Generate the InterceptsLocation attribute if we need interceptors (for local or cross-assembly handlers)
        bool needsInterceptors = handlersWithInfo.Count > 0 || crossAssemblyCallSites.Count > 0;
        if (needsInterceptors)
        {
            InterceptsLocationGenerator.Execute(context, configuration);
        }

        // Generate cross-assembly interceptors if there are call sites to handlers in referenced assemblies
        if (crossAssemblyCallSites.Count > 0)
        {
            CrossAssemblyInterceptorGenerator.Execute(context, crossAssemblyHandlers, crossAssemblyCallSites.ToImmutableArray(), configuration);
        }

        if (handlersWithInfo.Count == 0)
        {
            sw.Stop();
            GeneratorDiagnostics.LogExecute(
                compilation.AssemblyName ?? "Unknown",
                handlersWithInfo.Count,
                allMiddleware.Count,
                callSites.Length,
                crossAssemblyHandlers.Count,
                sw.ElapsedMilliseconds);
            return;
        }

        // Generate shared async helpers once per assembly (used by all handlers)
        HelpersGenerator.Execute(context, configuration);

        HandlerGenerator.Execute(context, handlersWithInfo, configuration);

        sw.Stop();
        GeneratorDiagnostics.LogExecute(
            compilation.AssemblyName ?? "Unknown",
            handlersWithInfo.Count,
            allMiddleware.Count,
            callSites.Length,
            crossAssemblyHandlers.Count,
            sw.ElapsedMilliseconds);
    }

    private static EquatableArray<MiddlewareInfo> GetApplicableMiddlewares(ImmutableArray<MiddlewareInfo> middlewares, HandlerInfo handler, Compilation compilation)
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
            .OrderBy(m => m.Order ?? int.MaxValue) // Middleware without order goes last
            .ThenBy(m => m.MessageType.IsObject ? 2 : (m.MessageType.IsInterface ? 1 : 0)) // Priority: specific=0, interface=1, object=2
            .ToArray());
    }

    private static bool IsMiddlewareApplicableToHandler(MiddlewareInfo middleware, HandlerInfo handler)
    {
        if (middleware.MessageType.IsObject)
            return true;

        if (middleware.MessageType.FullName == handler.MessageType.FullName)
            return true;

        // Check if middleware message type is an interface implemented by the handler message type
        if (middleware.MessageType.IsInterface && handler.MessageInterfaces.Contains(middleware.MessageType.FullName))
            return true;

        // Check if middleware message type is a base class of the handler message type
        if (handler.MessageBaseClasses.Contains(middleware.MessageType.FullName))
            return true;

        return false;
    }
}

