using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

/// <summary>
/// Generates minimal API endpoints from handler information.
/// </summary>
internal static class EndpointGenerator
{
    /// <summary>
    /// Generates the design-time API surface (stub) for endpoint extension methods.
    /// Registered via RegisterSourceOutput so IntelliSense sees Map{X}Endpoints()
    /// before the first build. Delegates to a static partial void core method
    /// that is a no-op at design time.
    /// </summary>
    public static void ExecuteStub(
        SourceProductionContext context,
        GeneratorConfiguration configuration,
        EndpointDefaultsInfo endpointDefaults,
        CompilationInfo compilationInfo)
    {
        // Only generate stub when minimal APIs are available and endpoint discovery is enabled
        if (!compilationInfo.SupportsMinimalApis)
            return;

        if (endpointDefaults.Discovery is "None" or null)
            return;

        var safeAssemblyName = compilationInfo.AssemblyName.ToIdentifier();

        // --- Per-module stub: {SafeAssemblyName}_MediatorEndpoints ---
        var moduleSource = new IndentedStringBuilder();
        moduleSource.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, "_MediatorEndpoints.Api.g.cs");
        moduleSource.AppendLine("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using System.Diagnostics.CodeAnalysis;
            """);

        moduleSource.AppendLine();
        moduleSource.AppendLine("namespace Foundatio.Mediator;");
        moduleSource.AppendLine();
        moduleSource.AddGeneratedCodeAttribute();
        moduleSource.AppendLines($$"""
            [ExcludeFromCodeCoverage]
            public static partial class {{safeAssemblyName}}_MediatorEndpoints
            {
                /// <summary>
                /// Maps this module's handler endpoints to the application.
                /// </summary>
                /// <param name="endpoints">The endpoint route builder.</param>
                /// <param name="logEndpoints">When true, logs all mapped endpoints at startup.</param>
                public static void MapEndpoints(IEndpointRouteBuilder endpoints, bool logEndpoints = false)
                {
                    MapEndpointsCore(endpoints, logEndpoints);
                }

                /// <summary>
                /// Core endpoint registration, implemented by the source generator at compile time.
                /// At design time this is a no-op so IntelliSense works before the first build.
                /// </summary>
                static partial void MapEndpointsCore(IEndpointRouteBuilder endpoints, bool logEndpoints);
            }
            """);

        context.AddSource("_MediatorEndpoints.Api.g.cs", moduleSource.ToString());

        // --- Aggregator stub: MapMediatorEndpoints() (app projects only) ---
        if (!compilationInfo.IsApplication)
            return;

        var aggSource = new IndentedStringBuilder();
        aggSource.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, "_MediatorEndpointAggregator.Api.g.cs");
        aggSource.AppendLine("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using System;
            using System.Diagnostics.CodeAnalysis;

            namespace Foundatio.Mediator;
            """);

        aggSource.AppendLine();
        aggSource.AddGeneratedCodeAttribute();
        aggSource.AppendLines("""
            [ExcludeFromCodeCoverage]
            public static partial class MediatorEndpointExtensions
            {
                /// <summary>
                /// Maps all discovered mediator handler endpoints from all referenced assemblies.
                /// Discovers endpoint modules automatically via <see cref="FoundatioModuleAttribute"/> and naming convention.
                /// </summary>
                /// <param name="endpoints">The endpoint route builder.</param>
                /// <param name="configure">Optional configuration to select assemblies and enable logging.</param>
                /// <returns>The endpoint route builder for chaining.</returns>
                public static IEndpointRouteBuilder MapMediatorEndpoints(this IEndpointRouteBuilder endpoints, Action<MediatorEndpointOptionsBuilder>? configure = null)
                {
                    MapMediatorEndpointsCore(endpoints, configure);
                    return endpoints;
                }

                /// <summary>
                /// Core aggregator implementation, filled in at compile time by the source generator.
                /// </summary>
                static partial void MapMediatorEndpointsCore(IEndpointRouteBuilder endpoints, Action<MediatorEndpointOptionsBuilder>? configure);
            }
            """);

        context.AddSource("_MediatorEndpointAggregator.Api.g.cs", aggSource.ToString());
    }

    /// <summary>
    /// Executes endpoint generation for handlers.
    /// </summary>
    public static void Execute(
        SourceProductionContext context,
        List<HandlerInfo> handlers,
        EndpointDefaultsInfo endpointDefaults,
        GeneratorConfiguration configuration,
        CompilationInfo compilationInfo)
    {
        // Check if the compilation supports minimal APIs
        if (!compilationInfo.SupportsMinimalApis)
            return;

        // Filter handlers that should generate endpoints based on discovery mode
        var endpointHandlers = GetEndpointHandlers(handlers, endpointDefaults);

        // Collect handlers that were skipped from endpoint generation (with reasons)
        var skippedHandlers = handlers
            .Where(h => h.Endpoint is { GenerateEndpoint: false, ExcludeReason: not null } && !h.MessageType.IsInterface && !h.IsGenericHandlerClass)
            .ToList();

        if (endpointHandlers.Count == 0 && !compilationInfo.IsApplication)
            return;

        if (endpointHandlers.Count > 0)
        {
            // Validate endpoint configurations and emit diagnostics
            ValidateEndpoints(context, endpointHandlers, endpointDefaults);

            // Generate the per-module endpoint registration code
            var source = GenerateEndpointCode(endpointHandlers, skippedHandlers, endpointDefaults, configuration, compilationInfo);
            context.AddSource("_MediatorEndpoints.g.cs", source);
        }

        // Generate the aggregator implementation in application projects
        if (compilationInfo.IsApplication)
        {
            var aggSource = GenerateAggregatorCode(configuration, compilationInfo);
            context.AddSource("_MediatorEndpointAggregator.g.cs", aggSource);

            // Generate the API version matcher policy and OpenAPI provider when versioning is enabled
            if (endpointDefaults.ApiVersions.Any())
            {
                var policySource = GenerateMatcherPolicyCode(configuration, endpointDefaults);
                context.AddSource("_ApiVersionMatcherPolicy.g.cs", policySource);

                var providerSource = GenerateOpenApiProviderCode(configuration, endpointDefaults);
                context.AddSource("_ApiVersionOpenApiProvider.g.cs", providerSource);
            }
        }
    }

    /// <summary>
    /// Filters handlers based on endpoint discovery mode.
    /// </summary>
    private static List<HandlerInfo> GetEndpointHandlers(List<HandlerInfo> handlers, EndpointDefaultsInfo endpointDefaults)
    {
        return endpointDefaults.Discovery switch
        {
            "Explicit" => handlers
                .Where(h => h.Endpoint is { GenerateEndpoint: true, HasExplicitEndpointAttribute: true } && !h.MessageType.IsInterface && !h.IsGenericHandlerClass)
                .ToList(),
            "All" => handlers
                .Where(h => h.Endpoint is { GenerateEndpoint: true } && !h.MessageType.IsInterface && !h.IsGenericHandlerClass)
                .ToList(),
            _ => [] // "None" mode - no endpoints generated
        };
    }

    /// <summary>
    /// Validates endpoint configurations and emits diagnostics for common issues.
    /// </summary>
    private static void ValidateEndpoints(SourceProductionContext context, List<HandlerInfo> handlers, EndpointDefaultsInfo endpointDefaults)
    {
        var warnedGroups = new HashSet<string>(StringComparer.Ordinal);

        foreach (var handler in handlers)
        {
            var endpoint = handler.Endpoint!.Value;

            // FMED015: Group route prefix duplicates global endpoint prefix.
            // Only applies to relative prefixes (no leading /) since absolute prefixes bypass the global group.
            var globalPrefix = endpointDefaults.RoutePrefix;
            var groupPrefix = endpoint.GroupRoutePrefix;
            if (!endpoint.GroupBypassGlobalPrefix
                && !string.IsNullOrEmpty(globalPrefix)
                && !string.IsNullOrEmpty(groupPrefix))
            {
                // For relative prefixes, check if the prefix content duplicates the global prefix content.
                // e.g. global = "/api", relative group = "api/products" → /api/api/products (wrong)
                var globalContent = globalPrefix!.TrimStart('/');
                var groupContent = groupPrefix!.TrimStart('/');
                if (groupContent.StartsWith(globalContent, StringComparison.OrdinalIgnoreCase)
                    && groupContent.Length > globalContent.Length
                    && warnedGroups.Add(groupPrefix))
                {
                    var suggested = groupContent.Substring(globalContent.Length).TrimStart('/');
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "FMED015",
                            "Group route prefix duplicates global endpoint prefix",
                            "HandlerEndpointGroup RoutePrefix '{0}' starts with the global EndpointRoutePrefix '{1}' content, which will produce a doubled path. " +
                            "Remove the duplicated portion (e.g. use '{2}' instead), or prefix with '/' for an absolute path that bypasses the global prefix.",
                            "Foundatio.Mediator",
                            DiagnosticSeverity.Warning,
                            isEnabledByDefault: true),
                        Location.None,
                        groupPrefix,
                        globalPrefix,
                        suggested));
                }
            }

            // FMED014: GET/DELETE endpoints where the message type cannot be constructed
            if (endpoint.HttpMethod is "GET" or "DELETE"
                && !endpoint.BindFromBody
                && !endpoint.SupportsAsParameters
                && !endpoint.HasParameterlessConstructor
                && endpoint.RouteParameters.Length == 0
                && endpoint.QueryParameters.Length == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "FMED014",
                        "Endpoint message type requires a parameterless constructor",
                        "Message type '{0}' used for {1} endpoint '{2}' has no public parameterless constructor and no route/query parameters. " +
                        "The generated code will fail to compile. Add a parameterless constructor, use a record with default parameter values, or add properties to bind from the route/query string.",
                        "Foundatio.Mediator",
                        DiagnosticSeverity.Warning,
                        isEnabledByDefault: true),
                    Location.None,
                    handler.MessageType.FullName,
                    endpoint.HttpMethod,
                    endpoint.Route));
            }
        }

        // FMED017: Overlapping API versions on the same route.
        // When versioning is enabled, multiple endpoints sharing a route+method must not
        // serve the same API version — that would make routing nondeterministic.
        if (endpointDefaults.ApiVersions.Any())
        {
            var byRouteMethod = handlers
                .Where(h => h.Endpoint!.Value.ApiVersions.Any())
                .GroupBy(h => h.Endpoint!.Value.HttpMethod.ToUpperInvariant() + " " + h.Endpoint!.Value.Route.ToLowerInvariant())
                .Select(g => g.ToList())
                .Where(endpointsInGroup => endpointsInGroup.Count >= 2);

            foreach (var endpointsInGroup in byRouteMethod)
            {
                // Collect all versions per handler and find overlaps
                for (var i = 0; i < endpointsInGroup.Count; i++)
                {
                    var versionsA = endpointsInGroup[i].Endpoint!.Value.ApiVersions;
                    for (var j = i + 1; j < endpointsInGroup.Count; j++)
                    {
                        var versionsB = endpointsInGroup[j].Endpoint!.Value.ApiVersions;
                        var overlap = versionsA.Intersect(versionsB, StringComparer.OrdinalIgnoreCase).ToList();
                        if (overlap.Count > 0)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                new DiagnosticDescriptor(
                                    "FMED017",
                                    "Ambiguous API version on endpoint route",
                                    "Handlers '{0}' and '{1}' both serve version(s) {2} on {3} {4}. " +
                                    "Each version must map to exactly one handler for a given route. " +
                                    "Remove the overlapping version from one of the handlers.",
                                    "Foundatio.Mediator",
                                    DiagnosticSeverity.Error,
                                    isEnabledByDefault: true),
                                Location.None,
                                endpointsInGroup[i].Identifier + "." + endpointsInGroup[i].MethodName,
                                endpointsInGroup[j].Identifier + "." + endpointsInGroup[j].MethodName,
                                string.Join(", ", overlap),
                                endpointsInGroup[i].Endpoint!.Value.HttpMethod,
                                endpointsInGroup[i].Endpoint!.Value.Route));
                        }
                    }
                }
            }
        }

        // FMED016: Handlers in the same class produce routes with different base paths.
        // Only check handlers without an explicit [HandlerEndpointGroup] group,
        // since grouped handlers already have a shared group prefix.
        var ungroupedByClass = handlers
            .Where(h => string.IsNullOrEmpty(h.Endpoint!.Value.GroupRoutePrefix) && !h.Endpoint!.Value.HasExplicitRoute)
            .GroupBy(h => h.Identifier);

        foreach (var group in ungroupedByClass)
        {
            var routePrefixes = group
                .Select(h =>
                {
                    var route = h.Endpoint!.Value.Route.TrimStart('/');
                    var slashIndex = route.IndexOf('/');
                    return slashIndex > 0 ? route.Substring(0, slashIndex) : route;
                })
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (routePrefixes.Count > 1)
            {
                var handlerName = group.Key;
                var prefixList = string.Join(", ", routePrefixes.Select(p => "/" + p));
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "FMED016",
                        "Handler methods generate routes with different base paths",
                        "Handler methods on '{0}' generate endpoints with different route prefixes ({1}). " +
                        "This usually means message names don't share a common entity root. " +
                        "Consider renaming messages to use a consistent noun (e.g., GetTodo instead of GetTodoById), " +
                        "or use [HandlerEndpointGroup(\"{2}\")] to group them under a shared prefix.",
                        "Foundatio.Mediator",
                        DiagnosticSeverity.Warning,
                        isEnabledByDefault: true),
                    Location.None,
                    handlerName,
                    prefixList,
                    handlerName.Replace("Handler", "").Replace("Consumer", "") + "s"));
            }
        }
    }

    /// <summary>
    /// Generates the complete endpoint registration source code.
    /// </summary>
    private static string GenerateEndpointCode(List<HandlerInfo> handlers, List<HandlerInfo> skippedHandlers, EndpointDefaultsInfo endpointDefaults, GeneratorConfiguration configuration, CompilationInfo compilationInfo)
    {
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, "_MediatorEndpoints.g.cs");

        // Check for available ASP.NET Core features
        bool hasAsParametersAttribute = compilationInfo.HasAsParametersAttribute;
        bool hasFromBodyAttribute = compilationInfo.HasFromBodyAttribute;
        bool hasWithOpenApi = compilationInfo.HasWithOpenApi;

        var safeAssemblyName = compilationInfo.AssemblyName.ToIdentifier();

        // The result mapper still uses a per-assembly suffix to avoid collisions
        var assemblySuffix = safeAssemblyName;

        // Check if any handler uses SSE streaming
        bool hasAnySseEndpoints = handlers.Any(h => h.Endpoint is { IsStreaming: true, StreamingFormat: "ServerSentEvents" });

        source.AppendLine("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using System.Diagnostics.CodeAnalysis;
            using System.Linq;
            """);

        if (hasAnySseEndpoints)
        {
            source.AppendLine("using System.Net.ServerSentEvents;");
        }

        if (compilationInfo.HasLoggerFactory)
        {
            source.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            source.AppendLine("using Microsoft.Extensions.Logging;");
        }

        source.AppendLine();
        source.AppendLine("namespace Foundatio.Mediator;");

        source.AppendLine();
        source.AppendLine($"public static partial class {safeAssemblyName}_MediatorEndpoints");
        source.AppendLine("{");
        source.IncrementIndent();

        // Generate the core implementation as a static partial void method
        // that fills in the stub declared in _MediatorEndpoints.Api.g.cs
        GenerateMapMediatorEndpointsCoreMethod(source, handlers, skippedHandlers, endpointDefaults, configuration, hasAsParametersAttribute, hasFromBodyAttribute, hasWithOpenApi, assemblySuffix, compilationInfo.HasLoggerFactory);

        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();

        // Generate the result mapper class (uses assembly suffix to avoid collisions)
        GenerateResultMapperClass(source, assemblySuffix);

        return source.ToString();
    }

    /// <summary>
    /// Generates the MapEndpointsCore static partial void implementation.
    /// This provides the compile-time body for the stub declared in _MediatorEndpoints.Api.g.cs.
    /// </summary>
    private static void GenerateMapMediatorEndpointsCoreMethod(
        IndentedStringBuilder source,
        List<HandlerInfo> handlers,
        List<HandlerInfo> skippedHandlers,
        EndpointDefaultsInfo endpointDefaults,
        GeneratorConfiguration configuration,
        bool hasAsParametersAttribute,
        bool hasFromBodyAttribute,
        bool hasWithOpenApi,
        string assemblySuffix,
        bool hasLoggerFactory)
    {
        source.AppendLine("/// <summary>");
        source.AppendLine("/// Core endpoint registration implementation.");
        source.AppendLine("/// </summary>");
        source.AppendLine("static partial void MapEndpointsCore(IEndpointRouteBuilder endpoints, bool logEndpoints)");
        source.AppendLine("{");
        source.IncrementIndent();

        // Determine the parent variable name for endpoint groups
        string parentGroupVar = "endpoints";

        // Emit global root group if a global route prefix is configured
        bool hasGlobalGroup = !string.IsNullOrEmpty(endpointDefaults.RoutePrefix);
        if (hasGlobalGroup)
        {
            source.AppendLine();
            source.AppendLine("// Global route prefix");
            source.Append($"var rootGroup = endpoints.MapGroup(\"{endpointDefaults.RoutePrefix}\")");

            // Apply global auth
            if (endpointDefaults.RequireAuth)
            {
                if (endpointDefaults.Policies.Any())
                {
                    foreach (var p in endpointDefaults.Policies)
                        source.Append($".RequireAuthorization(\"{p}\")");
                }
                else if (endpointDefaults.Roles.Any())
                {
                    var rolesStr = string.Join("\", \"", endpointDefaults.Roles);
                    source.Append($".RequireAuthorization(policy => policy.RequireRole(\"{rolesStr}\"))");
                }
                else
                    source.Append(".RequireAuthorization()");
            }

            source.AppendLine(";");

            // Apply global filters
            foreach (var filter in endpointDefaults.Filters)
            {
                source.AppendLine($"rootGroup.AddEndpointFilter<{filter}>();");
            }

            parentGroupVar = "rootGroup";
        }
        else if (endpointDefaults.Filters.Any() || endpointDefaults.RequireAuth)
        {
            // No route prefix but have global filters/auth — use empty group
            source.AppendLine();
            source.AppendLine("// Global endpoint defaults");
            source.Append("var rootGroup = endpoints.MapGroup(\"\")");

            if (endpointDefaults.RequireAuth)
            {
                if (endpointDefaults.Policies.Any())
                {
                    foreach (var p in endpointDefaults.Policies)
                        source.Append($".RequireAuthorization(\"{p}\")");
                }
                else if (endpointDefaults.Roles.Any())
                {
                    var rolesStr = string.Join("\", \"", endpointDefaults.Roles);
                    source.Append($".RequireAuthorization(policy => policy.RequireRole(\"{rolesStr}\"))");
                }
                else
                    source.Append(".RequireAuthorization()");
            }

            source.AppendLine(";");

            foreach (var filter in endpointDefaults.Filters)
            {
                source.AppendLine($"rootGroup.AddEndpointFilter<{filter}>();");
            }

            parentGroupVar = "rootGroup";
        }

        // Collect endpoint info for startup logging
        var endpointLogEntries = new List<(string HttpMethod, string FullRoute, string HandlerInfo, bool IsExplicitRoute)>();

        // All handlers are emitted on flat routes (no version path segments).
        // Version dispatch is handled via request headers when multiple handlers
        // map to the same route with different ApiVersions.
        EmitCategoryEndpoints(source, handlers, parentGroupVar, hasGlobalGroup,
            endpointDefaults, hasAsParametersAttribute, hasFromBodyAttribute, hasWithOpenApi,
            assemblySuffix, endpointLogEntries);

        // Collect skipped handler info for logging
        var skippedLogEntries = new List<(string HandlerInfo, string Reason)>();
        foreach (var handler in skippedHandlers)
        {
            skippedLogEntries.Add((
                $"{handler.Identifier}.{handler.MethodName}({handler.MessageType.Identifier})",
                handler.Endpoint!.Value.ExcludeReason!));
        }

        // Emit endpoint logging block
        EmitEndpointLogging(source, endpointLogEntries, skippedLogEntries, hasLoggerFactory);

        source.DecrementIndent();
        source.AppendLine("}");
    }

    /// <summary>
    /// Emits grouped endpoint registrations under a given parent group variable.
    /// When header-based versioning is enabled, detects route collisions across versions
    /// and emits version dispatch endpoints that read the Api-Version header.
    /// </summary>
    private static void EmitCategoryEndpoints(
        IndentedStringBuilder source,
        List<HandlerInfo> handlers,
        string parentGroupVar,
        bool hasGlobalGroup,
        EndpointDefaultsInfo endpointDefaults,
        bool hasAsParametersAttribute,
        bool hasFromBodyAttribute,
        bool hasWithOpenApi,
        string assemblySuffix,
        List<(string HttpMethod, string FullRoute, string HandlerInfo, bool IsExplicitRoute)> endpointLogEntries)
    {
        if (handlers.Count == 0)
            return;

        // Group handlers by group name
        var handlersByGroup = handlers
            .GroupBy(h => h.Endpoint?.GroupName ?? "Default")
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var handlerGroup in handlersByGroup)
        {
            var group = handlerGroup.Key;
            var groupHandlers = handlerGroup.ToList();

            // Get group route prefix from first handler
            var firstEndpoint = groupHandlers.First().Endpoint!.Value;
            var routePrefix = firstEndpoint.GroupRoutePrefix ?? "";

            // When the group uses an absolute prefix (leading /), bypass the global route prefix
            var groupParent = (firstEndpoint.GroupBypassGlobalPrefix && hasGlobalGroup)
                ? "endpoints"
                : parentGroupVar;

            source.AppendLine();
            source.AppendLine($"// {group} endpoints");

            // Create route group
            var groupVarName = $"{group.ToCamelCase()}Group";

            source.Append($"var {groupVarName} = {groupParent}.MapGroup(\"{routePrefix}\")");

            // Only add tag if group is explicitly defined (not "Default")
            if (group != "Default")
            {
                var groupTags = firstEndpoint.GroupTags;
                if (groupTags.Any())
                {
                    var tagsArgs = string.Join(", ", groupTags.Select(t => $"\"{t}\""));
                    source.Append($".WithTags({tagsArgs})");
                }
                else
                {
                    source.Append($".WithTags(\"{group}\")");
                }
            }

            // Add group-level auth if the group requires auth (and global doesn't already)
            var groupRequireAuth = firstEndpoint.RequireAuth && !endpointDefaults.RequireAuth;
            if (groupRequireAuth && !firstEndpoint.Policies.Any() && !firstEndpoint.Roles.Any())
            {
                source.Append(".RequireAuthorization()");
            }

            source.AppendLine(";");

            // Apply group-level filters
            var groupFilters = firstEndpoint.GroupFilters;
            foreach (var filter in groupFilters)
            {
                source.AppendLine($"{groupVarName}.AddEndpointFilter<{filter}>();");
            }

            source.AppendLine();

            // Detect duplicate routes and resolve conflicts (version-aware)
            var routeOverrides = ResolveDuplicateRoutes(groupHandlers);

            // Generate endpoints — each handler becomes its own endpoint
            // When versioning is enabled, the MatcherPolicy disambiguates same-route endpoints
            foreach (var handler in groupHandlers)
            {
                var handlerKey = HandlerGenerator.GetHandlerClassName(handler);
                routeOverrides.TryGetValue(handlerKey, out var routeOverride);

                var targetGroup = handler.Endpoint!.Value.RouteBypassPrefixes
                    ? "endpoints"
                    : groupVarName;

                GenerateEndpoint(source, handler, targetGroup, hasAsParametersAttribute, hasFromBodyAttribute, hasWithOpenApi, groupRequireAuth || endpointDefaults.RequireAuth, assemblySuffix, endpointDefaults.SummaryStyle, endpointDefaults, routeOverride);

                var endpointRoute = routeOverride ?? handler.Endpoint!.Value.Route;
                var fullRoute = ComputeFullDisplayRoute(
                    endpointDefaults.RoutePrefix, routePrefix, endpointRoute,
                    firstEndpoint.GroupBypassGlobalPrefix,
                    handler.Endpoint!.Value.RouteBypassPrefixes);
                endpointLogEntries.Add((
                    handler.Endpoint!.Value.HttpMethod,
                    fullRoute,
                    $"{handler.Identifier}.{handler.MethodName}({handler.MessageType.Identifier})",
                    handler.Endpoint!.Value.HasExplicitRoute));
            }
        }
    }

    /// <summary>
    /// Emits the endpoint logging block at the end of MapEndpointsCore.
    /// </summary>
    private static void EmitEndpointLogging(
        IndentedStringBuilder source,
        List<(string HttpMethod, string FullRoute, string HandlerInfo, bool IsExplicitRoute)> entries,
        List<(string HandlerInfo, string Reason)> skippedEntries,
        bool hasLoggerFactory)
    {
        if (entries.Count == 0 && skippedEntries.Count == 0)
            return;

        var maxMethodLen = entries.Count > 0 ? entries.Max(e => e.HttpMethod.Length) : 0;
        var maxRouteLen = entries.Count > 0 ? entries.Max(e => e.FullRoute.Length) : 0;

        source.AppendLine();
        source.AppendLine("// Log mapped endpoints when requested");
        source.AppendLine("if (logEndpoints)");
        source.AppendLine("{");
        source.IncrementIndent();

        if (hasLoggerFactory)
        {
            source.AppendLine("var endpointLogger = endpoints.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(\"Foundatio.Mediator.Endpoints\");");
            source.AppendLine("System.Action<string> writeLog = endpointLogger != null");
            source.IncrementIndent();
            source.AppendLine("? msg => endpointLogger.LogInformation(\"{MediatorEndpointInfo}\", msg)");
            source.AppendLine(": System.Console.WriteLine;");
            source.DecrementIndent();
        }
        else
        {
            source.AppendLine("System.Action<string> writeLog = System.Console.WriteLine;");
        }

        source.AppendLine($"writeLog(\"Foundatio.Mediator mapped {entries.Count} endpoint(s):\");");

        foreach (var (httpMethod, fullRoute, handlerInfo, isExplicitRoute) in entries)
        {
            var paddedMethod = httpMethod.PadRight(maxMethodLen);
            var paddedRoute = fullRoute.PadRight(maxRouteLen);
            var routeSource = isExplicitRoute ? "explicit" : "convention";
            source.AppendLine($"writeLog(\"  {paddedMethod}  {paddedRoute}  \u2192 {handlerInfo} ({routeSource})\");");
        }

        if (skippedEntries.Count > 0)
        {
            source.AppendLine($"writeLog(\"Foundatio.Mediator skipped {skippedEntries.Count} handler(s) from endpoint generation:\");");
            foreach (var (handlerInfo, reason) in skippedEntries)
            {
                source.AppendLine($"writeLog(\"  SKIP  {handlerInfo} ({reason})\");");
            }
        }

        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine("else");
        source.AppendLine("{");
        source.IncrementIndent();

        if (hasLoggerFactory)
        {
            source.AppendLine("var endpointLogger = endpoints.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(\"Foundatio.Mediator.Endpoints\");");
            source.AppendLine("if (endpointLogger != null)");
            source.IncrementIndent();
            source.AppendLine($"endpointLogger.LogInformation(\"Foundatio.Mediator mapped {entries.Count} endpoint(s).\");");
            source.DecrementIndent();
            source.AppendLine("else");
            source.IncrementIndent();
            source.AppendLine($"System.Console.WriteLine(\"Foundatio.Mediator mapped {entries.Count} endpoint(s).\");");
            source.DecrementIndent();
        }
        else
        {
            source.AppendLine($"System.Console.WriteLine(\"Foundatio.Mediator mapped {entries.Count} endpoint(s).\");");
        }

        source.DecrementIndent();
        source.AppendLine("}");
    }

    /// <summary>
    /// Computes the full display route path for logging by combining global prefix, group prefix, and endpoint route.
    /// </summary>
    private static string ComputeFullDisplayRoute(string? globalPrefix, string groupPrefix, string endpointRoute, bool groupBypassGlobalPrefix, bool routeBypassPrefixes)
    {
        string result;
        if (routeBypassPrefixes)
            result = endpointRoute;
        else if (groupBypassGlobalPrefix)
            result = JoinRouteParts(groupPrefix, endpointRoute);
        else
        {
            var basePath = globalPrefix ?? "";
            result = JoinRouteParts(basePath, JoinRouteParts(groupPrefix, endpointRoute));
        }

        if (string.IsNullOrEmpty(result))
            return "/";
        if (!result.StartsWith("/"))
            result = "/" + result;
        return result;
    }

    private static string JoinRouteParts(string a, string b)
    {
        a = a.TrimEnd('/');
        b = b.TrimStart('/');
        if (string.IsNullOrEmpty(a)) return b;
        if (string.IsNullOrEmpty(b)) return a;
        return a + "/" + b;
    }

    /// <summary>
    /// Detects duplicate routes within a group and returns overrides for conflicting handlers.
    /// Only handlers without an explicitly-set route will be given an override.
    /// The first handler in each duplicate group keeps its original route.
    /// When versioning is enabled, handlers on the same route with different version constraints
    /// are NOT considered duplicates (the MatcherPolicy disambiguates them at routing time).
    /// </summary>
    private static Dictionary<string, string> ResolveDuplicateRoutes(List<HandlerInfo> handlers)
    {
        var routeOverrides = new Dictionary<string, string>();

        // Group handlers by their generated route key (HTTP method + route)
        var routeGroups = handlers
            .Select(h => new
            {
                Handler = h,
                Endpoint = h.Endpoint!.Value,
                Route = GenerateRelativeRoute(h.Endpoint!.Value),
                // Use unique handler key that includes message type
                UniqueKey = HandlerGenerator.GetHandlerClassName(h)
            })
            .GroupBy(x => $"{x.Endpoint.HttpMethod}:{x.Route.TrimStart('/')}")
            .ToList();

        foreach (var group in routeGroups)
        {
            if (group.Count() <= 1)
                continue;

            // Within a route group, partition by version sets to find true duplicates.
            // Handlers with non-overlapping version constraints are handled by MatcherPolicy.
            var versionGroups = group
                .GroupBy(x =>
                {
                    var versions = x.Endpoint.ApiVersions;
                    return versions.Any()
                        ? string.Join(",", versions.OrderBy(v => v))
                        : ""; // empty = unversioned/fallback
                })
                .ToList();

            // If every handler has a unique version set, no duplicates
            if (versionGroups.All(vg => vg.Count() == 1))
                continue;

            // Reroute duplicates within each version group
            foreach (var vg in versionGroups.Where(vg => vg.Count() > 1))
            {
                foreach (var item in vg.Skip(1).Where(x => !x.Endpoint.HasExplicitRoute))
                {
                    // Use kebab-case message name as the route
                    var messageName = item.Handler.MessageType.Identifier;
                    var kebabRoute = "/" + messageName.ToKebabCase();
                    routeOverrides[item.UniqueKey] = kebabRoute;
                }
            }
        }

        return routeOverrides;
    }

    /// <summary>
    /// Generates a single endpoint registration.
    /// </summary>
    private static void GenerateEndpoint(
        IndentedStringBuilder source,
        HandlerInfo handler,
        string groupVarName,
        bool hasAsParametersAttribute,
        bool hasFromBodyAttribute,
        bool hasWithOpenApi,
        bool groupRequireAuth,
        string assemblySuffix,
        string summaryStyle,
        EndpointDefaultsInfo endpointDefaults,
        string? routeOverride = null)
    {
        var endpoint = handler.Endpoint!.Value;
        var httpMethod = endpoint.HttpMethod;
        var route = routeOverride ?? GenerateRelativeRoute(endpoint);
        var wrapperClassName = HandlerGenerator.GetHandlerClassName(handler);

        // Determine the Map method
        string mapMethod = httpMethod switch
        {
            "GET" => "MapGet",
            "POST" => "MapPost",
            "PUT" => "MapPut",
            "DELETE" => "MapDelete",
            "PATCH" => "MapPatch",
            _ => "MapPost"
        };

        source.AppendLine($"// {httpMethod} {route} - {handler.MessageType.Identifier}");
        source.Append($"{groupVarName}.{mapMethod}(\"{route}\", ");

        // Generate the lambda
        GenerateEndpointLambda(source, handler, endpoint, hasAsParametersAttribute, hasFromBodyAttribute, wrapperClassName, assemblySuffix);

        source.AppendLine(")");
        source.IncrementIndent();

        // Add metadata
        var endpointName = endpoint.Name;
        bool versioningEnabled = endpointDefaults.ApiVersions.Any();
        bool handlerHasVersions = endpoint.ApiVersions.Any();

        // Append version suffix to endpoint name for versioned handlers
        if (versioningEnabled && handlerHasVersions)
        {
            var versionSuffix = endpoint.ApiVersions.Length == 1
                ? endpoint.ApiVersions[0]
                : string.Join("_", endpoint.ApiVersions);
            endpointName = $"{endpointName}_v{versionSuffix}";
        }

        source.AppendLine($".WithName(\"{endpointName}\")");

        // Add version metadata for MatcherPolicy disambiguation
        if (versioningEnabled)
        {
            var defaultVersion = endpointDefaults.ApiVersions.Last();
            if (handlerHasVersions)
            {
                var versionsArray = string.Join(", ", endpoint.ApiVersions.Select(v => $"\"{v}\""));
                source.AppendLine($".WithMetadata(new Foundatio.Mediator.ApiVersionMetadata(new[] {{ {versionsArray} }}, \"{endpointDefaults.ApiVersionHeader}\", \"{defaultVersion}\"))");
            }
            else
            {
                source.AppendLine($".WithMetadata(new Foundatio.Mediator.ApiVersionMetadata(System.Array.Empty<string>(), \"{endpointDefaults.ApiVersionHeader}\", \"{defaultVersion}\"))");
            }
        }

        var messageName = summaryStyle == "Spaced"
            ? SplitPascalCase(handler.MessageType.Identifier)
            : handler.MessageType.Identifier;
        source.AppendLine($".WithSummary(\"{messageName}\")");

        // Use explicit description if provided, otherwise fall back to the summary text (which includes XML doc)
        var descriptionText = endpoint.Description ?? endpoint.Summary;
        if (!string.IsNullOrEmpty(descriptionText))
        {
            var escapedDescription = EscapeString(descriptionText!);
            source.AppendLine($".WithDescription(\"{escapedDescription}\")");
        }

        // Mark endpoint as deprecated in OpenAPI metadata
        if (endpoint.Deprecated)
        {
            source.AppendLine(".WithMetadata(new System.ObsoleteAttribute(\"This API version is deprecated.\"))");
        }

        // Add AllowAnonymous if handler opts out of group-level auth
        if (endpoint.AllowAnonymous)
        {
            source.AppendLine(".AllowAnonymous()");
        }

        // Add endpoint-specific auth if different from group
        if (!endpoint.AllowAnonymous && endpoint.RequireAuth && !groupRequireAuth)
        {
            if (endpoint.Policies.Any())
            {
                foreach (var policy in endpoint.Policies)
                {
                    source.AppendLine($".RequireAuthorization(\"{policy}\")");
                }
            }
            else if (endpoint.Roles.Any())
            {
                var rolesStr = string.Join("\", \"", endpoint.Roles);
                source.AppendLine($".RequireAuthorization(policy => policy.RequireRole(\"{rolesStr}\"))");
            }
            else
            {
                source.AppendLine(".RequireAuthorization()");
            }
        }
        else if (!endpoint.AllowAnonymous && endpoint.Policies.Any())
        {
            foreach (var policy in endpoint.Policies)
            {
                source.AppendLine($".RequireAuthorization(\"{policy}\")");
            }
        }
        else if (!endpoint.AllowAnonymous && endpoint.Roles.Any())
        {
            var rolesStr = string.Join("\", \"", endpoint.Roles);
            source.AppendLine($".RequireAuthorization(policy => policy.RequireRole(\"{rolesStr}\"))");
        }

        if (hasWithOpenApi)
        {
            source.AppendLine(".WithOpenApi()");
        }

        // Add Produces<T> metadata from return type
        if (endpoint.IsStreaming && endpoint.StreamingFormat == "ServerSentEvents")
        {
            // SSE endpoints produce text/event-stream; TypedResults.ServerSentEvents()
            // handles the content type header at runtime, but we add metadata for OpenAPI.
            source.AppendLine(".Produces(200, contentType: \"text/event-stream\")");
        }
        else if (!string.IsNullOrEmpty(endpoint.ProducesType))
        {
            // Use explicit SuccessStatusCode if set, otherwise 201 when Result.Created() detected, else 200
            var statusCode = endpoint.ExplicitSuccessStatusCode > 0
                ? endpoint.ExplicitSuccessStatusCode.ToString()
                : endpoint.UsesResultCreated ? "201" : "200";
            source.AppendLine($".Produces<{endpoint.ProducesType}>({statusCode})");
        }

        // Add additional ProducesProblem metadata from [HandlerEndpoint(ProducesStatusCodes = [...])]
        foreach (var statusCode in endpoint.ProducesStatusCodes)
        {
            source.AppendLine($".ProducesProblem({statusCode})");
        }

        // Add endpoint-level filters
        foreach (var filter in endpoint.Filters)
        {
            source.AppendLine($".AddEndpointFilter<{filter}>()");
        }

        source.DecrementIndent();
        source.AppendLine(";");
        source.AppendLine();
    }

    /// <summary>
    /// Generates the endpoint lambda expression.
    /// </summary>
    private static void GenerateEndpointLambda(
        IndentedStringBuilder source,
        HandlerInfo handler,
        EndpointInfo endpoint,
        bool hasAsParametersAttribute,
        bool hasFromBodyAttribute,
        string wrapperClassName,
        string assemblySuffix)
    {
        var messageType = handler.MessageType.FullName;
        var isAsync = handler.IsAsync;
        var asyncKeyword = isAsync ? "async " : "";

        if (endpoint.BindFromBody)
        {
            var routeParams = endpoint.RouteParameters;
            var fromBodyAttr = hasFromBodyAttribute ? "[Microsoft.AspNetCore.Mvc.FromBody] " : "";

            if (routeParams.Any())
            {
                // PUT/PATCH with route parameters - need to merge route params with body
                source.Append($"{asyncKeyword}(");

                // Add route parameters first
                for (int i = 0; i < routeParams.Length; i++)
                {
                    var param = routeParams[i];
                    source.Append($"{param.Type.FullName} {param.Name}");
                    source.Append(", ");
                }

                // Then add body parameter
                source.Append($"{fromBodyAttr}{messageType} message, ");
                source.Append("Foundatio.Mediator.IMediator mediator, System.Threading.CancellationToken cancellationToken) =>");
                source.AppendLine();
                source.AppendLine("{");
                source.IncrementIndent();

                // Merge route parameters into message
                if (handler.MessageType.IsRecord)
                {
                    // For records, use 'with' expression
                    source.Append("var mergedMessage = message with { ");
                    source.Append(string.Join(", ", routeParams.Select(p => $"{p.PropertyName} = {p.Name}")));
                    source.AppendLine(" };");
                }
                else
                {
                    // For classes, set properties directly (assumes they have setters)
                    foreach (var param in routeParams)
                    {
                        source.AppendLine($"message.{param.PropertyName} = {param.Name};");
                    }
                    source.AppendLine("var mergedMessage = message;");
                }

                GenerateHandlerCall(source, handler, wrapperClassName, "mergedMessage", isAsync, assemblySuffix);

                source.DecrementIndent();
                source.Append("}");
            }
            else
            {
                // POST without route parameters - just bind from body
                source.Append($"{asyncKeyword}({fromBodyAttr}{messageType} message, ");
                source.Append("Foundatio.Mediator.IMediator mediator, System.Threading.CancellationToken cancellationToken) =>");
                source.AppendLine();
                source.AppendLine("{");
                source.IncrementIndent();

                GenerateHandlerCall(source, handler, wrapperClassName, "message", isAsync, assemblySuffix);

                source.DecrementIndent();
                source.Append("}");
            }
        }
        else if (endpoint.SupportsAsParameters && hasAsParametersAttribute)
        {
            // GET/DELETE with [AsParameters] - message type supports it
            source.Append($"{asyncKeyword}([Microsoft.AspNetCore.Http.AsParameters] {messageType} message, ");
            source.Append("Foundatio.Mediator.IMediator mediator, System.Threading.CancellationToken cancellationToken) =>");
            source.AppendLine();
            source.AppendLine("{");
            source.IncrementIndent();

            GenerateHandlerCall(source, handler, wrapperClassName, "message", isAsync, assemblySuffix);

            source.DecrementIndent();
            source.Append("}");
        }
        else
        {
            // GET/DELETE with constructor binding - need to construct the message
            var routeParams = endpoint.RouteParameters;
            var queryParams = endpoint.QueryParameters;
            var allParams = routeParams.Concat(queryParams).ToList();

            source.Append($"{asyncKeyword}(");

            // Add route and query parameters
            for (int i = 0; i < allParams.Count; i++)
            {
                var param = allParams[i];
                var paramType = param.Type.FullName;

                // Add FromQuery for query parameters
                if (!param.IsRouteParameter)
                {
                    source.Append("[Microsoft.AspNetCore.Mvc.FromQuery] ");
                }

                source.Append($"{paramType} {param.Name}");

                if (i < allParams.Count - 1)
                    source.Append(", ");
            }

            if (allParams.Count > 0)
                source.Append(", ");

            source.Append("Foundatio.Mediator.IMediator mediator, System.Threading.CancellationToken cancellationToken) =>");
            source.AppendLine();
            source.AppendLine("{");
            source.IncrementIndent();

            // Construct the message from parameters
            if (allParams.Count > 0)
            {
                source.Append($"var message = new {messageType}(");
                source.Append(string.Join(", ", allParams.Select(p => $"{p.PropertyName}: {p.Name}")));
                source.AppendLine(");");
            }
            else
            {
                source.AppendLine($"var message = new {messageType}();");
            }

            GenerateHandlerCall(source, handler, wrapperClassName, "message", isAsync, assemblySuffix);

            source.DecrementIndent();
            source.Append("}");
        }
    }

    /// <summary>
    /// Generates the handler call and result mapping.
    /// </summary>
    private static void GenerateHandlerCall(
        IndentedStringBuilder source,
        HandlerInfo handler,
        string wrapperClassName,
        string messageVar,
        bool isAsync,
        string assemblySuffix)
    {
        var handlerMethodName = HandlerGenerator.GetHandlerMethodName(handler);
        var awaitKeyword = isAsync ? "await " : "";

        // Check if the return type is void - don't assign to variable
        if (handler.ReturnType.IsVoid)
        {
            source.AppendLine($"{awaitKeyword}global::Foundatio.Mediator.Generated.{wrapperClassName}.{handlerMethodName}(mediator, {messageVar}, cancellationToken);");
            source.AppendLine("return Microsoft.AspNetCore.Http.Results.Ok();");
        }
        else if (handler.ReturnType.IsFileResult)
        {
            source.AppendLine($"var result = {awaitKeyword}global::Foundatio.Mediator.Generated.{wrapperClassName}.{handlerMethodName}(mediator, {messageVar}, cancellationToken);");
            source.AppendLine($"return MediatorEndpointResultMapper_{assemblySuffix}.ToHttpResult(result);");
        }
        else if (handler.ReturnType.IsResult)
        {
            source.AppendLine($"var result = {awaitKeyword}global::Foundatio.Mediator.Generated.{wrapperClassName}.{handlerMethodName}(mediator, {messageVar}, cancellationToken);");
            source.AppendLine($"return MediatorEndpointResultMapper_{assemblySuffix}.ToHttpResult(result);");
        }
        else if (handler.ReturnType.IsTuple && handler.ReturnType.TupleItems.Length > 0)
        {
            // For tuple handlers, use mediator.InvokeAsync<T>() which handles cascading internally
            // and returns only the first tuple item (the result value).
            var firstItem = handler.ReturnType.TupleItems[0];
            source.AppendLine($"var result = await mediator.InvokeAsync<{firstItem.TypeFullName}>({messageVar}, cancellationToken);");
            if (firstItem.IsResult)
                source.AppendLine($"return MediatorEndpointResultMapper_{assemblySuffix}.ToHttpResult(result);");
            else
                source.AppendLine("return Microsoft.AspNetCore.Http.Results.Ok(result);");
        }
        else if (handler.Endpoint is { IsStreaming: true })
        {
            // Streaming endpoint — IAsyncEnumerable<T>
            var endpoint = handler.Endpoint.Value;
            source.AppendLine($"var stream = {awaitKeyword}global::Foundatio.Mediator.Generated.{wrapperClassName}.{handlerMethodName}(mediator, {messageVar}, cancellationToken);");
            if (endpoint.StreamingFormat == "ServerSentEvents")
            {
                // Wrap in TypedResults.ServerSentEvents() for SSE format
                var eventTypeArg = !string.IsNullOrEmpty(endpoint.SseEventType)
                    ? $", eventType: \"{endpoint.SseEventType}\""
                    : "";
                source.AppendLine($"return Microsoft.AspNetCore.Http.TypedResults.ServerSentEvents(stream{eventTypeArg});");
            }
            else
            {
                // Default: return IAsyncEnumerable directly — ASP.NET serializes as JSON array
                source.AppendLine("return Microsoft.AspNetCore.Http.Results.Ok(stream);");
            }
        }
        else
        {
            source.AppendLine($"var result = {awaitKeyword}global::Foundatio.Mediator.Generated.{wrapperClassName}.{handlerMethodName}(mediator, {messageVar}, cancellationToken);");
            source.AppendLine("return Microsoft.AspNetCore.Http.Results.Ok(result);");
        }
    }

    /// <summary>
    /// Generates the MediatorEndpointResultMapper class.
    /// </summary>
    private static void GenerateResultMapperClass(IndentedStringBuilder source, string assemblySuffix)
    {
        source.AddGeneratedCodeAttribute();
        source.AppendLines($$"""
            [ExcludeFromCodeCoverage]
            public static class MediatorEndpointResultMapper_{{assemblySuffix}}
            {
                /// <summary>
                /// Converts a Foundatio.Mediator.IResult to an HTTP result.
                /// </summary>
                public static Microsoft.AspNetCore.Http.IResult ToHttpResult(Foundatio.Mediator.IResult result)
                {
                    return result.Status switch
                    {
                        Foundatio.Mediator.ResultStatus.Success => result.GetValue() switch
                        {
                            Foundatio.Mediator.FileResult file => Microsoft.AspNetCore.Http.Results.File(
                                file.Stream, file.ContentType, file.FileName),
                            { } v => Microsoft.AspNetCore.Http.Results.Ok(v),
                            _ => Microsoft.AspNetCore.Http.Results.Ok()
                        },
                        Foundatio.Mediator.ResultStatus.Created => Microsoft.AspNetCore.Http.Results.Created(
                            result.Location ?? "", result.GetValue()),
                        Foundatio.Mediator.ResultStatus.NoContent => Microsoft.AspNetCore.Http.Results.NoContent(),
                        Foundatio.Mediator.ResultStatus.BadRequest => Microsoft.AspNetCore.Http.Results.BadRequest(
                            string.IsNullOrEmpty(result.Message) ? null : new { message = result.Message }),
                        Foundatio.Mediator.ResultStatus.Invalid => Microsoft.AspNetCore.Http.Results.ValidationProblem(
                            result.ValidationErrors
                                .GroupBy(e => e.Identifier ?? "")
                                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
                        Foundatio.Mediator.ResultStatus.NotFound => Microsoft.AspNetCore.Http.Results.NotFound(
                            string.IsNullOrEmpty(result.Message) ? null : new { message = result.Message }),
                        Foundatio.Mediator.ResultStatus.Unauthorized => Microsoft.AspNetCore.Http.Results.Unauthorized(),
                        Foundatio.Mediator.ResultStatus.Forbidden => Microsoft.AspNetCore.Http.Results.Forbid(),
                        Foundatio.Mediator.ResultStatus.Conflict => Microsoft.AspNetCore.Http.Results.Conflict(
                            string.IsNullOrEmpty(result.Message) ? null : new { message = result.Message }),
                        Foundatio.Mediator.ResultStatus.Error => Microsoft.AspNetCore.Http.Results.Problem(
                            result.Message ?? "An error occurred", statusCode: 500),
                        Foundatio.Mediator.ResultStatus.CriticalError => Microsoft.AspNetCore.Http.Results.Problem(
                            result.Message ?? "A critical error occurred", statusCode: 500),
                        Foundatio.Mediator.ResultStatus.Unavailable => Microsoft.AspNetCore.Http.Results.Problem(
                            result.Message ?? "Service unavailable", statusCode: 503),
                        _ => Microsoft.AspNetCore.Http.Results.Problem("An unexpected error occurred", statusCode: 500)
                    };
                }
            }
            """);
    }

    /// <summary>
    /// Gets the route for the endpoint (already relative to the group).
    /// </summary>
    private static string GenerateRelativeRoute(EndpointInfo endpoint)
    {
        // The route is already stored as a relative route
        return endpoint.Route;
    }

    /// <summary>
    /// Splits a PascalCase identifier into space-separated words (e.g., "GetProduct" becomes "Get Product").
    /// </summary>
    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var sb = new StringBuilder(value.Length + 4);

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            // Replace underscores with spaces
            if (c == '_')
            {
                // Avoid double spaces or leading/trailing spaces
                if (sb.Length > 0 && sb[sb.Length - 1] != ' ')
                    sb.Append(' ');
                continue;
            }

            if (i > 0 && char.IsUpper(c) && sb.Length > 0 && sb[sb.Length - 1] != ' ')
            {
                // Don't add space between consecutive uppercase (e.g., "IO" stays "IO")
                bool prevIsUpper = char.IsUpper(value[i - 1]) || value[i - 1] == '_';
                bool nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);

                if (!prevIsUpper || nextIsLower)
                    sb.Append(' ');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates the API version matcher policy that disambiguates endpoints by version header.
    /// Only emitted in application projects when API versioning is enabled.
    /// </summary>
    private static string GenerateMatcherPolicyCode(GeneratorConfiguration configuration, EndpointDefaultsInfo endpointDefaults)
    {
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, "_ApiVersionMatcherPolicy.g.cs");
        source.AppendLine("""
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.AspNetCore.Routing.Matching;
            using Microsoft.Extensions.DependencyInjection;
            using System;
            using System.Collections.Generic;
            using System.Diagnostics.CodeAnalysis;
            using System.Linq;
            using System.Threading.Tasks;

            namespace Foundatio.Mediator;
            """);

        // Emit the all-versions array as a static field
        var allVersionsLiteral = string.Join(", ", endpointDefaults.ApiVersions.Select(v => $"\"{v}\""));

        source.AppendLine();
        source.AddGeneratedCodeAttribute();
        source.AppendLines($$"""
            [ExcludeFromCodeCoverage]
            internal sealed class ApiVersionMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy
            {
                private static readonly string[] s_allVersions = new[] { {{allVersionsLiteral}} };

                public override int Order => -100;

                public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints)
                {
                    for (var i = 0; i < endpoints.Count; i++)
                    {
                        if (endpoints[i].Metadata.GetMetadata<ApiVersionMetadata>() != null)
                            return true;
                    }
                    return false;
                }

                public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
                {
                    // Collect candidate version arrays and resolve the requested version
                    string? requestedVersion = null;
                    bool headerWasExplicit = false;
                    string? versionHeader = null;
                    var candidateVersions = new string[]?[candidates.Count];

                    for (var i = 0; i < candidates.Count; i++)
                    {
                        if (!candidates.IsValidCandidate(i)) continue;
                        var meta = candidates[i].Endpoint.Metadata.GetMetadata<ApiVersionMetadata>();
                        if (meta == null) continue;

                        candidateVersions[i] = meta.Versions;

                        // Resolve the requested version once from the first annotated candidate
                        if (requestedVersion == null)
                        {
                            versionHeader = meta.VersionHeader;
                            var headerValue = httpContext.Request.Headers[meta.VersionHeader].FirstOrDefault();
                            if (headerValue != null)
                            {
                                requestedVersion = headerValue;
                                headerWasExplicit = true;
                            }
                            else
                            {
                                requestedVersion = meta.DefaultVersion;
                            }
                        }
                    }

                    if (requestedVersion == null) return Task.CompletedTask;

                    // Reject explicitly-provided versions that are not in the declared set
                    if (headerWasExplicit && Array.IndexOf(s_allVersions, requestedVersion) < 0)
                    {
                        // Case-insensitive check
                        bool found = false;
                        for (int i = 0; i < s_allVersions.Length; i++)
                        {
                            if (string.Equals(s_allVersions[i], requestedVersion, StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            httpContext.Response.StatusCode = 400;
                            httpContext.Response.ContentType = "application/json";
                            httpContext.Response.Headers["Api-Version-Supported"] = string.Join(", ", s_allVersions);
                            // Invalidate all candidates to prevent handler execution
                            for (var i = 0; i < candidates.Count; i++)
                                candidates.SetValidity(i, false);
                            // Write body to start the response and prevent the routing pipeline from overwriting the status code
                            return httpContext.Response.WriteAsync("{\"error\":\"Unsupported API version. Check Api-Version-Supported response header for valid versions.\"}");
                        }
                    }

                    // Set version context for downstream middleware and handlers
                    var versionContext = httpContext.RequestServices.GetService<Foundatio.Mediator.ApiVersionContext>();
                    if (versionContext != null)
                        versionContext.Current = requestedVersion;

                    var (winner, hasVersioned) = ApiVersionMetadata.ResolveWinner(candidateVersions, requestedVersion, s_allVersions);

                    if (!hasVersioned) return Task.CompletedTask;

                    // Invalidate non-winning candidates
                    for (var i = 0; i < candidates.Count; i++)
                    {
                        if (i == winner || !candidates.IsValidCandidate(i)) continue;
                        if (candidates[i].Endpoint.Metadata.GetMetadata<ApiVersionMetadata>() != null)
                            candidates.SetValidity(i, false);
                    }

                    return Task.CompletedTask;
                }
            }
            """);

        return source.ToString();
    }

    /// <summary>
    /// Generates an IApiDescriptionProvider that assigns OpenAPI group names based on ApiVersionMetadata.
    /// Versioned endpoints are assigned to their version's group; fallback endpoints are cloned into all groups.
    /// This ensures each per-version OpenAPI document contains the correct endpoints.
    /// </summary>
    private static string GenerateOpenApiProviderCode(GeneratorConfiguration configuration, EndpointDefaultsInfo endpointDefaults)
    {
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, "_ApiVersionOpenApiProvider.g.cs");
        source.AppendLine("""
            using System;
            using System.Collections.Generic;
            using System.Diagnostics.CodeAnalysis;
            using System.Linq;

            namespace Foundatio.Mediator;
            """);

        var versionsLiteral = string.Join(", ", endpointDefaults.ApiVersions.Select(v => $"\"{v}\""));

        source.AppendLine();
        source.AddGeneratedCodeAttribute();
        source.AppendLines($$"""
            [ExcludeFromCodeCoverage]
            internal sealed class ApiVersionOpenApiProvider : Microsoft.AspNetCore.Mvc.ApiExplorer.IApiDescriptionProvider
            {
                private static readonly string[] DeclaredVersions = new[] { {{versionsLiteral}} };

                public int Order => 1000;

                public void OnProvidersExecuting(Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescriptionProviderContext context) { }

                public void OnProvidersExecuted(Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescriptionProviderContext context)
                {
                    // Pass 1: Collect versioned route signatures so fallbacks can be skipped when superseded
                    var versionedRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var result in context.Results)
                    {
                        ApiVersionMetadata? vm = null;
                        if (result.ActionDescriptor.EndpointMetadata is { } m1)
                        {
                            foreach (var obj in m1)
                            {
                                if (obj is ApiVersionMetadata m2) { vm = m2; break; }
                            }
                        }
                        if (vm != null && vm.Versions.Length > 0)
                        {
                            foreach (var v in vm.Versions)
                                versionedRoutes.Add(result.HttpMethod + "|" + (result.RelativePath ?? "") + "|" + v);
                        }
                    }

                    // Pass 2: Assign version groups
                    var toAdd = new List<Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription>();
                    var toRemove = new List<Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription>();

                    foreach (var result in context.Results)
                    {
                        ApiVersionMetadata? versionMeta = null;
                        if (result.ActionDescriptor.EndpointMetadata is { } meta)
                        {
                            foreach (var obj in meta)
                            {
                                if (obj is ApiVersionMetadata m) { versionMeta = m; break; }
                            }
                        }

                        if (versionMeta != null)
                        {
                            if (versionMeta.Versions.Length > 0)
                            {
                                // Versioned endpoint: assign to first version group, clone for additional versions
                                result.GroupName = ToGroupName(versionMeta.Versions[0]);
                                for (int i = 1; i < versionMeta.Versions.Length; i++)
                                    toAdd.Add(CloneWithGroupName(result, ToGroupName(versionMeta.Versions[i])));
                            }
                            else
                            {
                                // Fallback endpoint: only include in versions without a versioned replacement
                                var assigned = false;
                                for (int i = 0; i < DeclaredVersions.Length; i++)
                                {
                                    var key = result.HttpMethod + "|" + (result.RelativePath ?? "") + "|" + DeclaredVersions[i];
                                    if (versionedRoutes.Contains(key)) continue;
                                    if (!assigned) { result.GroupName = ToGroupName(DeclaredVersions[i]); assigned = true; }
                                    else toAdd.Add(CloneWithGroupName(result, ToGroupName(DeclaredVersions[i])));
                                }
                                if (!assigned) toRemove.Add(result);
                            }
                        }
                        else if (result.GroupName == null)
                        {
                            // Non-mediator ungrouped endpoint: clone into every version group
                            result.GroupName = ToGroupName(DeclaredVersions[0]);
                            for (int i = 1; i < DeclaredVersions.Length; i++)
                                toAdd.Add(CloneWithGroupName(result, ToGroupName(DeclaredVersions[i])));
                        }
                    }

                    foreach (var d in toAdd) context.Results.Add(d);
                    foreach (var d in toRemove) context.Results.Remove(d);
                }

                private static string ToGroupName(string version)
                    => version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : "v" + version;

                private static Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription CloneWithGroupName(
                    Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription source, string groupName)
                {
                    var clone = new Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription
                    {
                        ActionDescriptor = source.ActionDescriptor,
                        GroupName = groupName,
                        HttpMethod = source.HttpMethod,
                        RelativePath = source.RelativePath,
                    };
                    foreach (var p in source.ParameterDescriptions) clone.ParameterDescriptions.Add(p);
                    foreach (var f in source.SupportedRequestFormats) clone.SupportedRequestFormats.Add(f);
                    foreach (var f in source.SupportedResponseTypes) clone.SupportedResponseTypes.Add(f);
                    foreach (var kv in source.Properties) clone.Properties[kv.Key] = kv.Value;
                    return clone;
                }
            }
            """);

        return source.ToString();
    }

    /// <summary>
    /// Derives a clean project name from the assembly name for use as a suffix
    /// in generated endpoint method names. Takes the last meaningful segment,
    /// strips common suffixes like Api/Web/Module/Service/Server, and sanitizes.
    /// </summary>
    /// <summary>
    /// Generates the aggregator implementation that discovers and invokes all endpoint modules via reflection.
    /// </summary>
    private static string GenerateAggregatorCode(GeneratorConfiguration configuration, CompilationInfo compilationInfo)
    {
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, "_MediatorEndpointAggregator.g.cs");
        source.AppendLine("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using System;
            using System.Diagnostics.CodeAnalysis;
            using System.Linq;
            using System.Reflection;

            namespace Foundatio.Mediator;
            """);

        source.AppendLines("""

            public static partial class MediatorEndpointExtensions
            {
                /// <summary>
                /// Core aggregator implementation. Discovers all assemblies marked with
                /// <see cref="FoundatioModuleAttribute"/> containing a class ending with _MediatorEndpoints.
                /// </summary>
                static partial void MapMediatorEndpointsCore(IEndpointRouteBuilder endpoints, Action<MediatorEndpointOptionsBuilder>? configure)
                {
                    MediatorEndpointOptions? options = null;
                    if (configure != null)
                    {
                        var builder = new MediatorEndpointOptionsBuilder();
                        configure(builder);
                        options = builder.Build();
                    }

                    var logEndpoints = options?.LogEndpoints ?? false;

                    if (options?.Assemblies == null || options.Assemblies.Count == 0)
                        MediatorExtensions.EnsureReferencedAssembliesLoaded();

                    var assemblies = options?.Assemblies != null && options.Assemblies.Count > 0
                        ? options.Assemblies
                        : AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).ToList();

                    foreach (var assembly in assemblies)
                    {
                        if (!assembly.GetCustomAttributes(typeof(FoundatioModuleAttribute), false).Any())
                            continue;

                        foreach (var type in assembly.GetExportedTypes())
                        {
                            if (!type.Name.EndsWith("_MediatorEndpoints", StringComparison.Ordinal))
                                continue;

                            var method = type.GetMethod("MapEndpoints", BindingFlags.Public | BindingFlags.Static);
                            method?.Invoke(null, new object[] { endpoints, logEndpoints });
                        }
                    }
                }
            }
            """);

        return source.ToString();
    }

    /// <summary>
    /// Escapes a string for use in C# source code.
    /// </summary>
    private static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }
}
