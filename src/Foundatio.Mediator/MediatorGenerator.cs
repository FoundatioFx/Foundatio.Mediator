using System.Diagnostics;
using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

[Generator]
public sealed class MediatorGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InternalGeneratorError = new(
        "FMED998",
        "Internal source generator error",
        "Foundatio.Mediator generator failed: {0}",
        "Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var configAndEndpoints = context.CompilationProvider
            .Select(static (compilation, _) => GetConfigurationAndEndpointDefaults(compilation))
            .WithTrackingName(TrackingNames.Settings);

        var generatorConfiguration = configAndEndpoints.Select(static (pair, _) => pair.Configuration);
        var endpointDefaults = configAndEndpoints.Select(static (pair, _) => pair.EndpointDefaults);

        var compilationInfo = context.CompilationProvider
            .Select(static (compilation, _) => GetCompilationInfo(compilation))
            .WithTrackingName(TrackingNames.CompilationInfo);

        var crossAssemblyMiddleware = context.CompilationProvider
            .Select(static (compilation, _) => new EquatableArray<MiddlewareInfo>(MetadataMiddlewareScanner.ScanReferencedAssemblies(compilation).ToArray()))
            .WithTrackingName(TrackingNames.CrossAssemblyMiddleware);

        var crossAssemblyHandlers = context.CompilationProvider
            .Select(static (compilation, _) => new EquatableArray<HandlerInfo>(CrossAssemblyHandlerScanner.ScanReferencedAssemblies(compilation).ToArray()))
            .WithTrackingName(TrackingNames.CrossAssemblyHandlers);

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

        var combinedData = handlers.Collect()
            .Combine(middleware.Collect())
            .Combine(callSites.Collect())
            .Combine(generatorConfiguration)
            .Combine(endpointDefaults)
            .Combine(compilationInfo)
            .Combine(crossAssemblyMiddleware)
            .Combine(crossAssemblyHandlers)
            .Select(static (spc, _) => (
                Handlers: spc.Left.Left.Left.Left.Left.Left.Left,
                Middleware: spc.Left.Left.Left.Left.Left.Left.Right,
                CallSites: spc.Left.Left.Left.Left.Left.Right,
                Configuration: spc.Left.Left.Left.Left.Right,
                EndpointDefaults: spc.Left.Left.Left.Right,
                CompilationInfo: spc.Left.Left.Right,
                CrossAssemblyMiddleware: spc.Left.Right,
                CrossAssemblyHandlers: spc.Right
            ));

        context.RegisterImplementationSourceOutput(combinedData,
            static (spc, source) => Execute(
                source.Handlers, source.Middleware, source.CallSites,
                source.Configuration, source.EndpointDefaults, source.CompilationInfo,
                source.CrossAssemblyMiddleware, source.CrossAssemblyHandlers, spc));

    }

    /// <summary>
    /// Reads [assembly: MediatorConfiguration] and returns both parsed generator configuration and endpoint defaults in a single pass.
    /// </summary>
    private static (GeneratorConfiguration Configuration, EndpointDefaultsInfo EndpointDefaults) GetConfigurationAndEndpointDefaults(Compilation compilation)
    {
        var isCSharpSufficient = compilation is CSharpCompilation { LanguageVersion: LanguageVersion.Default or >= LanguageVersion.CSharp11 };

        var configAttr = compilation.Assembly.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.MediatorConfigurationAttribute);

        // Generator configuration defaults
        bool disableInterceptors = false;
        string handlerLifetime = WellKnownTypes.LifetimeNone;
        string middlewareLifetime = WellKnownTypes.LifetimeNone;
        bool disableOpenTelemetry = false;
        bool disableAuthorization = false;
        bool conventionalDiscoveryDisabled = false;
        bool generationCounterEnabled = false;
        string notificationPublishStrategy = "ForeachAwait";

        // Endpoint defaults
        string discovery = "All";
        string? routePrefix = "/api";
        var filters = Array.Empty<string>();
        bool requireAuth = false;
        var policies = Array.Empty<string>();
        var roles = Array.Empty<string>();
        string summaryStyle = "Exact";
        var apiVersions = Array.Empty<string>();
        string apiVersionHeader = "Api-Version";
        bool endpointConfigured = false;

        if (configAttr != null)
        {
            endpointConfigured = true;

            foreach (var arg in configAttr.NamedArguments)
            {
                switch (arg.Key)
                {
                    // Generator configuration
                    case "DisableInterceptors" when arg.Value.Value is bool b:
                        disableInterceptors = b;
                        break;
                    case "HandlerLifetime" when arg.Value.Value is int v:
                        handlerLifetime = v switch { 1 => WellKnownTypes.LifetimeTransient, 2 => WellKnownTypes.LifetimeScoped, 3 => WellKnownTypes.LifetimeSingleton, _ => WellKnownTypes.LifetimeNone };
                        break;
                    case "MiddlewareLifetime" when arg.Value.Value is int v:
                        middlewareLifetime = v switch { 1 => WellKnownTypes.LifetimeTransient, 2 => WellKnownTypes.LifetimeScoped, 3 => WellKnownTypes.LifetimeSingleton, _ => WellKnownTypes.LifetimeNone };
                        break;
                    case "DisableOpenTelemetry" when arg.Value.Value is bool b:
                        disableOpenTelemetry = b;
                        break;
                    case "DisableAuthorization" when arg.Value.Value is bool b:
                        disableAuthorization = b;
                        break;
                    case "HandlerDiscovery" when arg.Value.Value is int v:
                        conventionalDiscoveryDisabled = v == 1; // Explicit = 1
                        break;
                    case "NotificationPublishStrategy" when arg.Value.Value is int v:
                        notificationPublishStrategy = v switch { 1 => "TaskWhenAll", 2 => "FireAndForget", _ => "ForeachAwait" };
                        break;
                    case "EnableGenerationCounter" when arg.Value.Value is bool b:
                        generationCounterEnabled = b;
                        break;

                    // Endpoint defaults
                    case "EndpointDiscovery" when arg.Value.Value is int v:
                        discovery = v switch { 1 => "Explicit", 2 => "All", _ => "None" };
                        break;
                    case "EndpointRoutePrefix" when arg.Value.Value is string s:
                        routePrefix = s;
                        break;
                    case "EndpointFilters" when !arg.Value.IsNull && arg.Value.Kind == TypedConstantKind.Array:
                        filters = arg.Value.Values
                            .Where(v => v.Value is INamedTypeSymbol)
                            .Select(v => ((INamedTypeSymbol)v.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                            .ToArray();
                        break;
                    case "AuthorizationRequired" when arg.Value.Value is bool:
                        requireAuth = (bool)arg.Value.Value!;
                        break;
                    case "AuthorizationPolicies" when !arg.Value.IsNull && arg.Value.Kind == TypedConstantKind.Array:
                        policies = arg.Value.Values
                            .Where(v => v.Value is string)
                            .Select(v => (string)v.Value!)
                            .ToArray();
                        break;
                    case "AuthorizationRoles" when !arg.Value.IsNull && arg.Value.Kind == TypedConstantKind.Array:
                        roles = arg.Value.Values
                            .Where(v => v.Value is string)
                            .Select(v => (string)v.Value!)
                            .ToArray();
                        break;
                    case "EndpointSummaryStyle" when arg.Value.Value is int v:
                        summaryStyle = v switch { 1 => "Spaced", _ => "Exact" };
                        break;
                    case "ApiVersions" when !arg.Value.IsNull && arg.Value.Kind == TypedConstantKind.Array:
                        apiVersions = arg.Value.Values
                            .Where(v => v.Value is string)
                            .Select(v => (string)v.Value!)
                            .ToArray();
                        break;
                    case "ApiVersionHeader" when arg.Value.Value is string s:
                        apiVersionHeader = s;
                        break;
                }
            }
        }

        var interceptorsEnabled = !disableInterceptors && isCSharpSufficient;
        var openTelemetryEnabled = !disableOpenTelemetry;
        var authorizationEnabled = !disableAuthorization;

        var configuration = new GeneratorConfiguration(interceptorsEnabled, handlerLifetime, middlewareLifetime,
            openTelemetryEnabled, authorizationEnabled, conventionalDiscoveryDisabled, generationCounterEnabled, notificationPublishStrategy);

        var endpointDefaults = new EndpointDefaultsInfo
        {
            Discovery = discovery,
            RoutePrefix = routePrefix,
            Filters = new(filters),
            RequireAuth = requireAuth,
            Policies = new(policies),
            Roles = new(roles),
            SummaryStyle = summaryStyle,
            ApiVersions = new(apiVersions),
            ApiVersionHeader = apiVersionHeader,
            IsConfigured = endpointConfigured
        };

        return (configuration, endpointDefaults);
    }

    /// <summary>
    /// Extracts compilation-level capability flags so downstream generators don't need the raw Compilation.
    /// </summary>
    private static CompilationInfo GetCompilationInfo(Compilation compilation)
    {
        return new CompilationInfo(
            AssemblyName: compilation.AssemblyName ?? "Unknown",
            SupportsMinimalApis: compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder") != null,
            HasAsParametersAttribute: compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.AsParametersAttribute") != null,
            HasFromBodyAttribute: compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromBodyAttribute") != null,
            HasWithOpenApi: compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.OpenApiRouteHandlerBuilderExtensions") != null,
            IsAspNetCore: compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.IHttpContextAccessor") != null,
            HasLoggerFactory: compilation.GetTypeByMetadataName("Microsoft.Extensions.Logging.ILoggerFactory") != null,
            HasServerSentEvents: compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.TypedResults")?.GetMembers("ServerSentEvents").Any() == true,
            IsApplication: compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication);
    }

    private static void Execute(
        ImmutableArray<HandlerInfo> handlers,
        ImmutableArray<MiddlewareInfo> middleware,
        ImmutableArray<CallSiteInfo> callSites,
        GeneratorConfiguration configuration,
        EndpointDefaultsInfo endpointDefaults,
        CompilationInfo compilationInfo,
        EquatableArray<MiddlewareInfo> crossAssemblyMiddleware,
        EquatableArray<HandlerInfo> crossAssemblyHandlers,
        SourceProductionContext context)
    {
        try
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

            // Combine syntax-based middleware (from current assembly) with metadata-based middleware (from referenced assemblies)
            var allMiddleware = filteredMiddleware.ToList();
            allMiddleware.AddRange(crossAssemblyMiddleware);

            var crossAssemblyHandlerList = crossAssemblyHandlers.ToList();

            var callSitesByMessage = callSites.ToList()
                .Where(cs => !cs.IsPublish)
                .GroupBy(cs => cs.MessageType)
                .ToDictionary(g => g.Key, g => g.ToArray());

            // Track which call sites are handled by cross-assembly handlers
            var crossAssemblyCallSites = new List<CallSiteInfo>();
            var crossAssemblyHandlerMessageTypes = new HashSet<string>(crossAssemblyHandlerList.Select(h => h.MessageType.FullName));

            // Find message types with multiple local handlers — these are ambiguous and
            // must NOT have interceptors generated (would cause CS9153: intercepted multiple times).
            var ambiguousMessageTypes = new HashSet<string>(filteredHandlers
                .GroupBy(h => h.MessageType.FullName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key));

            var handlersWithInfo = new List<HandlerInfo>();
            foreach (var handler in filteredHandlers)
            {
                // Don't assign call sites when multiple handlers exist for the same message type,
                // since each would generate an interceptor for the same call site.
                CallSiteInfo[]? handlerCallSites = null;
                if (!ambiguousMessageTypes.Contains(handler.MessageType.FullName))
                    callSitesByMessage.TryGetValue(handler.MessageType, out handlerCallSites);

                var applicableMiddleware = GetApplicableMiddlewares(allMiddleware.ToImmutableArray(), handler, configuration, out var orderingDiagnostics);

                // Resolve effective handler lifetime: use explicit lifetime if set, otherwise use project default
                var resolvedHandlerLifetime = ResolveEffectiveLifetime(handler.Lifetime, configuration.DefaultHandlerLifetime);

                // Merge assembly-level authorization defaults into handler authorization info
                var mergedAuth = MergeAuthorizationDefaults(handler.Authorization, endpointDefaults);

                handlersWithInfo.Add(handler with { CallSites = new(handlerCallSites ?? []), Middleware = applicableMiddleware, Lifetime = resolvedHandlerLifetime, OrderingDiagnostics = new(orderingDiagnostics.ToArray()), Authorization = mergedAuth });
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
            HandlerGenerator.ValidateGlobalCallSites(context, handlersWithInfo, callSites, crossAssemblyHandlerList);

            // Generate assembly attribute and handlers registration if there are handlers, middleware, or versioning is enabled (enables cross-assembly discovery)
            bool versioningEnabled = endpointDefaults.ApiVersions.Any();
            if (handlersWithInfo.Count > 0 || middleware.Length > 0 || (versioningEnabled && compilationInfo.IsApplication && compilationInfo.IsAspNetCore))
            {
                FoundatioModuleGenerator.Execute(context, compilationInfo, handlersWithInfo, filteredMiddleware, configuration, endpointDefaults);
            }

            // Generate the InterceptsLocation attribute if we need interceptors (for local, cross-assembly, or publish handlers)
            bool hasPublishInterceptors = configuration.InterceptorsEnabled && callSites.Any(cs => cs.IsPublish && !cs.MessageType.IsTypeParameter);
            bool needsInterceptors = handlersWithInfo.Count > 0 || crossAssemblyCallSites.Count > 0 || hasPublishInterceptors;
            if (needsInterceptors)
            {
                InterceptsLocationGenerator.Execute(context, configuration);
            }

            // Generate cross-assembly interceptors if there are call sites to handlers in referenced assemblies
            if (crossAssemblyCallSites.Count > 0)
            {
                CrossAssemblyInterceptorGenerator.Execute(context, crossAssemblyHandlerList, crossAssemblyCallSites.ToImmutableArray(), configuration);
            }

            // Combine local and cross-assembly handlers for cascading message handler lookup
            var allHandlers = handlersWithInfo.Concat(crossAssemblyHandlerList).ToList();

            // Generate publish interceptors when interceptors are enabled
            if (configuration.InterceptorsEnabled)
            {
                var publishCallSites = callSites.Where(cs => cs.IsPublish && !cs.MessageType.IsTypeParameter).ToList();
                if (publishCallSites.Count > 0)
                {
                    PublishInterceptorGenerator.Execute(context, publishCallSites, allHandlers, configuration);
                }
            }

            // Generate endpoint registration for all handlers (local + cross-assembly)
            // This must happen before the early return so WebApp can generate endpoints for handlers in referenced modules
            EndpointGenerator.Execute(context, allHandlers, endpointDefaults, configuration, compilationInfo);

            if (handlersWithInfo.Count == 0)
            {
                sw.Stop();
                GeneratorDiagnostics.LogExecute(
                    compilationInfo.AssemblyName,
                    handlersWithInfo.Count,
                    allMiddleware.Count,
                    callSites.Length,
                    crossAssemblyHandlerList.Count,
                    sw.ElapsedMilliseconds);
                return;
            }

            // Generate shared async helpers once per assembly (used by all handlers)
            HelpersGenerator.Execute(context, configuration);

            HandlerGenerator.Execute(context, handlersWithInfo, allHandlers, configuration);

            sw.Stop();
            GeneratorDiagnostics.LogExecute(
                compilationInfo.AssemblyName,
                handlersWithInfo.Count,
                allMiddleware.Count,
                callSites.Length,
                crossAssemblyHandlerList.Count,
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InternalGeneratorError,
                Location.None,
                ex.ToString()));
        }
    }

    private static EquatableArray<MiddlewareInfo> GetApplicableMiddlewares(ImmutableArray<MiddlewareInfo> middlewares, HandlerInfo handler, GeneratorConfiguration configuration, out List<DiagnosticInfo> orderingDiagnostics)
    {
        orderingDiagnostics = [];
        var applicable = new List<MiddlewareInfo>();
        var addedMiddlewareTypes = new HashSet<string>();

        // Add middleware that matches by message type (global middleware)
        // Skip middleware marked as ExplicitOnly - those are only added via [UseMiddleware] references
        foreach (var middleware in middlewares)
        {
            if (middleware.ExplicitOnly)
                continue;

            if (IsMiddlewareApplicableToHandler(middleware, handler))
            {
                // Resolve effective middleware lifetime
                var resolvedLifetime = ResolveEffectiveLifetime(middleware.Lifetime, configuration.DefaultMiddlewareLifetime);
                applicable.Add(middleware with { Lifetime = resolvedLifetime });
                addedMiddlewareTypes.Add(middleware.FullName);
            }
        }

        // Add handler-specific middleware from [UseMiddleware] and custom attributes
        foreach (var reference in handler.HandlerMiddlewareReferences)
        {
            // Find the middleware info from the global list
            var middlewareInfo = middlewares.FirstOrDefault(m => m.FullName == reference.MiddlewareTypeName);
            if (middlewareInfo.FullName == null)
            {
                orderingDiagnostics.Add(new DiagnosticInfo
                {
                    Identifier = "FMED013",
                    Title = "Unknown middleware type",
                    Message = $"Middleware type '{reference.MiddlewareTypeName}' referenced by [UseMiddleware] on handler '{handler.FullName}' was not found. Ensure the type exists and ends with 'Middleware'.",
                    Severity = DiagnosticSeverity.Warning
                });
                continue;
            }

            // Resolve effective middleware lifetime
            var resolvedLifetime = ResolveEffectiveLifetime(middlewareInfo.Lifetime, configuration.DefaultMiddlewareLifetime);

            if (addedMiddlewareTypes.Contains(middlewareInfo.FullName))
            {
                // Middleware already added from message type matching
                // Update the order if the handler attribute specifies one
                if (reference.Order != int.MaxValue)
                {
                    var index = applicable.FindIndex(m => m.FullName == middlewareInfo.FullName);
                    if (index >= 0)
                    {
                        applicable[index] = applicable[index] with { Order = reference.Order, Lifetime = resolvedLifetime };
                    }
                }
            }
            else
            {
                // Add middleware with handler-specified order and resolved lifetime
                applicable.Add(middlewareInfo with { Order = reference.Order, Lifetime = resolvedLifetime });
                addedMiddlewareTypes.Add(middlewareInfo.FullName);
            }
        }

        // Use topological sort to respect OrderBefore/OrderAfter constraints with numeric Order as tiebreaker
        List<DiagnosticInfo> cycleDiagnostics;
        var sorted = MiddlewareOrderingSorter.Sort(
            applicable,
            m => m.FullName,
            m => (IEnumerable<string>)m.OrderBefore,
            m => (IEnumerable<string>)m.OrderAfter,
            m => m.Order ?? int.MaxValue,
            out cycleDiagnostics);

        // If there were no relative ordering constraints, apply the original secondary sort (message type specificity)
        // For items at the same numeric order level, prefer specific types over interfaces over object
        if (!applicable.Any(m => m.OrderBefore.Any() || m.OrderAfter.Any()))
        {
            sorted = sorted
                .OrderBy(m => m.Order ?? int.MaxValue)
                .ThenBy(m => m.MessageType.IsObject ? 2 : (m.MessageType.IsInterface ? 1 : 0))
                .ToList();
        }

        // Propagate cycle diagnostics to the caller for reporting
        if (cycleDiagnostics.Count > 0)
        {
            orderingDiagnostics.AddRange(cycleDiagnostics);
        }

        return new EquatableArray<MiddlewareInfo>(sorted.ToArray());
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

    /// <summary>
    /// Merges assembly-level authorization defaults into a handler's authorization info.
    /// Handler-level [HandlerAuthorize] takes precedence. If the handler doesn't have explicit auth,
    /// assembly-level AuthorizationRequired/AuthorizationPolicies/AuthorizationRoles are applied.
    /// </summary>
    private static AuthorizationInfo MergeAuthorizationDefaults(AuthorizationInfo handlerAuth, EndpointDefaultsInfo defaults)
    {
        // If the handler already has [HandlerAuthorize] or [HandlerAllowAnonymous], keep as-is
        if (handlerAuth.Required || handlerAuth.AllowAnonymous)
            return handlerAuth;

        // Apply assembly-level defaults if configured
        if (!defaults.RequireAuth)
            return handlerAuth;

        var roles = defaults.Roles.Any() ? defaults.Roles : handlerAuth.Roles;
        var policies = defaults.Policies.Any()
            ? defaults.Policies
            : handlerAuth.Policies;

        return new AuthorizationInfo
        {
            Required = true,
            AllowAnonymous = handlerAuth.AllowAnonymous,
            Roles = roles,
            Policies = policies,
        };
    }

    /// <summary>
    /// Resolves the effective lifetime by using the explicit lifetime if set, otherwise the default.
    /// Returns null if both are "None" (meaning no DI registration needed - use lazy caching).
    /// </summary>
    private static string? ResolveEffectiveLifetime(string? explicitLifetime, string defaultLifetime)
    {
        // If explicit lifetime is set and not "None", use it
        if (!string.IsNullOrEmpty(explicitLifetime) &&
            !string.Equals(explicitLifetime, WellKnownTypes.LifetimeNone, StringComparison.OrdinalIgnoreCase))
        {
            return explicitLifetime;
        }

        // Fall back to default lifetime if it's not "None"
        if (!string.Equals(defaultLifetime, WellKnownTypes.LifetimeNone, StringComparison.OrdinalIgnoreCase))
        {
            return defaultLifetime;
        }

        // Both are "None" - return null to indicate no DI registration
        return null;
    }
}

