using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

/// <summary>
/// Generates minimal API endpoints from handler information.
/// </summary>
internal static class EndpointGenerator
{
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
            var grpPrefix = endpoint.GroupRoutePrefix;
            if (!endpoint.GroupBypassGlobalPrefix
                && !string.IsNullOrEmpty(globalPrefix)
                && !string.IsNullOrEmpty(grpPrefix))
            {
                // For relative prefixes, check if the prefix content duplicates the global prefix content.
                // e.g. global = "/api", relative group = "api/products" → /api/api/products (wrong)
                var globalContent = globalPrefix!.TrimStart('/');
                var grpContent = grpPrefix!.TrimStart('/');
                if (grpContent.StartsWith(globalContent, StringComparison.OrdinalIgnoreCase)
                    && grpContent.Length > globalContent.Length
                    && warnedGroups.Add(grpPrefix))
                {
                    var suggested = grpContent.Substring(globalContent.Length).TrimStart('/');
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
                        grpPrefix,
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

        // FMED016: Handlers in the same class produce routes with different base paths.
        // Only check handlers without an explicit [HandlerEndpointGroup],
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
        source.AddGeneratedCodeAttribute();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine($"public static class {safeAssemblyName}_MediatorEndpoints");
        source.AppendLine("{");
        source.IncrementIndent();

        // Generate the MapEndpoints method
        GenerateMapEndpointsMethod(source, handlers, skippedHandlers, endpointDefaults, configuration, hasAsParametersAttribute, hasFromBodyAttribute, hasWithOpenApi, assemblySuffix, compilationInfo.HasLoggerFactory, compilationInfo.AssemblyName);

        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();

        // Generate the result mapper class (uses assembly suffix to avoid collisions)
        GenerateResultMapperClass(source, assemblySuffix);

        return source.ToString();
    }

    /// <summary>
    /// Generates the public MapEndpoints method for a per-module endpoint class.
    /// </summary>
    private static void GenerateMapEndpointsMethod(
        IndentedStringBuilder source,
        List<HandlerInfo> handlers,
        List<HandlerInfo> skippedHandlers,
        EndpointDefaultsInfo endpointDefaults,
        GeneratorConfiguration configuration,
        bool hasAsParametersAttribute,
        bool hasFromBodyAttribute,
        bool hasWithOpenApi,
        string assemblySuffix,
        bool hasLoggerFactory,
        string assemblyName)
    {
        source.AppendLine("/// <summary>");
        source.AppendLine("/// Maps this module's handler endpoints to the application.");
        source.AppendLine("/// </summary>");
        source.AppendLine("/// <param name=\"endpoints\">The endpoint route builder.</param>");
        source.AppendLine("/// <param name=\"logEndpoints\">When true, logs all mapped endpoints at startup.</param>");
        source.AppendLine("public static void MapEndpoints(IEndpointRouteBuilder endpoints, bool logEndpoints = false)");
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

        // Group handlers by endpoint group
        var handlersByGroup = handlers
            .GroupBy(h => h.Endpoint?.Group ?? "Default")
            .OrderBy(g => g.Key)
            .ToList();

        // Collect endpoint info for startup logging
        var endpointLogEntries = new List<(string HttpMethod, string FullRoute, string HandlerInfo, bool IsExplicitRoute)>();

        foreach (var endpointGroup in handlersByGroup)
        {
            var groupName = endpointGroup.Key;
            var groupHandlers = endpointGroup.ToList();

            // Get Group route prefix from first handler
            var firstEndpoint = groupHandlers.First().Endpoint!.Value;
            var routePrefix = firstEndpoint.GroupRoutePrefix ?? "";

            // When the group uses an absolute prefix (leading /), bypass the global route prefix
            var groupParent = (firstEndpoint.GroupBypassGlobalPrefix && hasGlobalGroup)
                ? "endpoints"
                : parentGroupVar;

            source.AppendLine();
            source.AppendLine($"// {groupName} endpoints");

            // Create route group for the group
            var groupVarName = $"{groupName.ToCamelCase()}Group";

            source.Append($"var {groupVarName} = {groupParent}.MapGroup(\"{routePrefix}\")");

            // Only add tag if group is explicitly defined (not "Default")
            if (groupName != "Default")
            {
                var groupTags = firstEndpoint.GroupTags;
                if (groupTags.Any())
                {
                    var tagsArgs = string.Join(", ", groupTags.Select(t => $"\"{t}\""));
                    source.Append($".WithTags({tagsArgs})");
                }
                else
                {
                    source.Append($".WithTags(\"{groupName}\")");
                }
            }

            // Add Group-level auth if the group requires auth (and global doesn't already)
            var groupRequireAuth = firstEndpoint.RequireAuth && !endpointDefaults.RequireAuth;
            if (groupRequireAuth && !firstEndpoint.Policies.Any() && !firstEndpoint.Roles.Any())
            {
                source.Append(".RequireAuthorization()");
            }

            source.AppendLine(";");

            // Apply Group-level filters
            var groupFilters = firstEndpoint.GroupFilters;
            foreach (var filter in groupFilters)
            {
                source.AppendLine($"{groupVarName}.AddEndpointFilter<{filter}>();");
            }

            // Apply Group-level conventions (IEndpointConvention<RouteGroupBuilder> from class + assembly-level attributes)
            var allGroupConventions = firstEndpoint.Conventions
                .Concat(endpointDefaults.Conventions)
                .Where(c => c.Scope != ConventionScope.Method && IsGroupBuilderConvention(c))
                .ToArray();
            var groupConventions = DeduplicateConventions(allGroupConventions);
            foreach (var convention in groupConventions)
            {
                EmitConventionCall(source, convention, groupVarName);
            }

            source.AppendLine();

            // Detect duplicate routes within this group and resolve conflicts
            var routeOverrides = ResolveDuplicateRoutes(groupHandlers);

            // Generate endpoint for each handler in the group
            foreach (var handler in groupHandlers)
            {
                // Check if this handler needs a route override due to conflict
                // Use the unique handler key that includes message type
                var handlerKey = HandlerGenerator.GetHandlerClassName(handler);
                routeOverrides.TryGetValue(handlerKey, out var routeOverride);

                // When the explicit route is absolute (leading /), bypass all prefixes
                var targetGroup = handler.Endpoint!.Value.RouteBypassPrefixes
                    ? "endpoints"
                    : groupVarName;

                GenerateEndpoint(source, handler, targetGroup, hasAsParametersAttribute, hasFromBodyAttribute, hasWithOpenApi, groupRequireAuth || endpointDefaults.RequireAuth, assemblySuffix, endpointDefaults.SummaryStyle, endpointDefaults.Conventions, routeOverride);

                // Collect endpoint info for logging
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

        // Collect skipped handler info for logging
        var skippedLogEntries = new List<(string HandlerInfo, string Reason)>();
        foreach (var handler in skippedHandlers)
        {
            skippedLogEntries.Add((
                $"{handler.Identifier}.{handler.MethodName}({handler.MessageType.Identifier})",
                handler.Endpoint!.Value.ExcludeReason!));
        }

        // Emit endpoint logging block
        EmitEndpointLogging(source, endpointLogEntries, skippedLogEntries, hasLoggerFactory, assemblyName);

        source.DecrementIndent();
        source.AppendLine("}");
    }

    /// <summary>
    /// Emits the endpoint logging block at the end of MapEndpoints.
    /// </summary>
    private static void EmitEndpointLogging(
        IndentedStringBuilder source,
        List<(string HttpMethod, string FullRoute, string HandlerInfo, bool IsExplicitRoute)> entries,
        List<(string HandlerInfo, string Reason)> skippedEntries,
        bool hasLoggerFactory,
        string assemblyName)
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

        source.AppendLine($"writeLog(\"Foundatio.Mediator mapped {entries.Count} endpoint(s) for {assemblyName}:\");");

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
            source.AppendLine($"endpointLogger.LogInformation(\"Foundatio.Mediator mapped {entries.Count} endpoint(s) for {assemblyName}.\");");
            source.DecrementIndent();
            source.AppendLine("else");
            source.IncrementIndent();
            source.AppendLine($"System.Console.WriteLine(\"Foundatio.Mediator mapped {entries.Count} endpoint(s) for {assemblyName}.\");");
            source.DecrementIndent();
        }
        else
        {
            source.AppendLine($"System.Console.WriteLine(\"Foundatio.Mediator mapped {entries.Count} endpoint(s) for {assemblyName}.\");");
        }

        source.DecrementIndent();
        source.AppendLine("}");
    }

    private static string ComputeFullDisplayRoute(string? globalPrefix, string groupPrefix, string endpointRoute, bool groupBypassGlobalPrefix, bool routeBypassPrefixes)
        => RouteConventions.ComputeFullDisplayRoute(globalPrefix, groupPrefix, endpointRoute, groupBypassGlobalPrefix, routeBypassPrefixes);

    private static string JoinRouteParts(string a, string b)
        => RouteConventions.JoinRouteParts(a, b);

    /// <summary>
    /// Detects duplicate routes within a group and returns overrides for conflicting handlers.
    /// Only handlers without an explicitly-set route will be given an override.
    /// The first handler in each duplicate group keeps its original route.
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
            .GroupBy(x => $"{x.Endpoint.HttpMethod}:{x.Route}")
            .Where(g => g.Count() > 1) // Only care about duplicates
            .ToList();

        foreach (var group in routeGroups)
        {
            // Skip the first handler - it keeps its original route
            // Only give non-explicit routes a message-based path for subsequent duplicates
            foreach (var item in group.Skip(1).Where(x => !x.Endpoint.HasExplicitRoute))
            {
                // Use kebab-case message name as the route
                var messageName = item.Handler.MessageType.Identifier;
                var kebabRoute = "/" + messageName.ToKebabCase();
                routeOverrides[item.UniqueKey] = kebabRoute;
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
        EquatableArray<EndpointConventionInfo> assemblyConventions,
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

        // Merge handler conventions with assembly-level conventions, then apply most-derived-wins dedup.
        // For endpoint builders: method + class + assembly-level endpoint conventions.
        var allEndpointConventions = endpoint.Conventions
            .Concat(assemblyConventions)
            .Where(c => c.Scope == ConventionScope.Method || IsEndpointBuilderConvention(c))
            .ToArray();
        var endpointConventions = DeduplicateConventions(allEndpointConventions);

        source.AppendLine($"// {httpMethod} {route} - {handler.MessageType.Identifier}");

        var epVarName = $"ep_{SanitizeIdentifier(handler.MessageType.QualifiedName)}";
        if (endpointConventions.Length > 0)
            source.Append($"var {epVarName} = {groupVarName}.{mapMethod}(\"{route}\", ");
        else
            source.Append($"{groupVarName}.{mapMethod}(\"{route}\", ");

        // Generate the lambda
        GenerateEndpointLambda(source, handler, endpoint, hasAsParametersAttribute, hasFromBodyAttribute, wrapperClassName, assemblySuffix);

        source.AppendLine(")");
        source.IncrementIndent();

        // Add metadata
        source.AppendLine($".WithName(\"{endpoint.Name}\")");

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

        // Apply endpoint-level conventions (IEndpointConvention<RouteHandlerBuilder> etc.)
        if (endpointConventions.Length > 0)
        {
            foreach (var convention in endpointConventions)
            {
                EmitConventionCall(source, convention, epVarName);
            }
        }

        source.AppendLine();
    }

    /// <summary>
    /// Determines if a convention targets an endpoint builder (RouteHandlerBuilder or IEndpointConventionBuilder).
    /// </summary>
    private static bool IsEndpointBuilderConvention(EndpointConventionInfo convention)
    {
        // RouteHandlerBuilder or IEndpointConventionBuilder (which RouteHandlerBuilder implements)
        return convention.BuilderTypeName.Contains("RouteHandlerBuilder")
            || convention.BuilderTypeName.Contains("IEndpointConventionBuilder");
    }

    /// <summary>
    /// Determines if a convention targets a group builder (RouteGroupBuilder).
    /// </summary>
    private static bool IsGroupBuilderConvention(EndpointConventionInfo convention)
    {
        return convention.BuilderTypeName.Contains("RouteGroupBuilder");
    }

    /// <summary>
    /// Applies most-derived-wins deduplication to conventions. For each attribute type,
    /// only the convention from the most specific scope is kept (Method &gt; Class &gt; Assembly).
    /// </summary>
    private static EndpointConventionInfo[] DeduplicateConventions(EndpointConventionInfo[] conventions)
    {
        if (conventions.Length <= 1)
            return conventions;

        return conventions
            .GroupBy(c => c.AttributeTypeName)
            .Select(g => g.OrderByDescending(c => c.Scope).First())
            .ToArray();
    }

    /// <summary>
    /// Emits code to instantiate a convention attribute and call Configure on the builder.
    /// </summary>
    private static void EmitConventionCall(
        IndentedStringBuilder source,
        EndpointConventionInfo convention,
        string builderVarName)
    {
        // Build constructor arguments string
        var ctorArgs = string.Join(", ", convention.ConstructorArguments
            .Select(a => a.Value ?? "default"));

        // Build property initializer list
        var namedArgs = convention.NamedArguments
            .Where(na => na.Value != null)
            .Select(na => $"{na.Name} = {na.Value}")
            .ToArray();

        var initializerBlock = namedArgs.Length > 0
            ? $" {{ {string.Join(", ", namedArgs)} }}"
            : "";

        // Determine which Configure method to call when the attribute implements multiple IEndpointConvention<T>
        var interfaceCast = $"(Foundatio.Mediator.IEndpointConvention<{convention.BuilderTypeName}>)";

        source.AppendLine(
            $"({interfaceCast}new {convention.AttributeTypeName}({ctorArgs}){initializerBlock}).Configure({builderVarName});");
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
            var bindingParams = endpoint.BindingParameters;
            var fromBodyAttr = hasFromBodyAttribute ? "[Microsoft.AspNetCore.Mvc.FromBody] " : "";
            var allMergeParams = routeParams.Concat(bindingParams).ToList();

            if (allMergeParams.Count > 0)
            {
                // POST/PUT/PATCH with route and/or binding parameters - need to merge into message
                source.Append($"{asyncKeyword}(");

                // Add route parameters first
                foreach (var param in routeParams)
                {
                    source.Append($"{param.Type.FullName} {param.Name}, ");
                }

                // Then add body parameter
                source.Append($"{fromBodyAttr}{messageType} message, ");

                // Add binding parameters (e.g., [FromHeader(Name = "X-Tenant-Id")] string tenantId)
                foreach (var param in bindingParams)
                {
                    source.Append($"{param.BindingAttributeSyntax} {param.Type.FullName} {param.Name}, ");
                }

                source.Append("Microsoft.AspNetCore.Http.HttpContext httpContext, Foundatio.Mediator.IMediator mediator, System.Threading.CancellationToken cancellationToken) =>");
                source.AppendLine();
                source.AppendLine("{");
                source.IncrementIndent();

                source.AppendLine("using var callContext = Foundatio.Mediator.CallContext.Rent().Set(httpContext).Set(httpContext.Request).Set(httpContext.Response).Set(httpContext.User);");

                // Merge route + binding parameters into message
                if (handler.MessageType.IsRecord)
                {
                    source.Append("var mergedMessage = message with { ");
                    source.Append(string.Join(", ", allMergeParams.Select(p => $"{p.PropertyName} = {p.Name}")));
                    source.AppendLine(" };");
                }
                else
                {
                    foreach (var param in allMergeParams)
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
                // POST without route or binding parameters - just bind from body
                source.Append($"{asyncKeyword}({fromBodyAttr}{messageType} message, ");
                source.Append("Microsoft.AspNetCore.Http.HttpContext httpContext, Foundatio.Mediator.IMediator mediator, System.Threading.CancellationToken cancellationToken) =>");
                source.AppendLine();
                source.AppendLine("{");
                source.IncrementIndent();

                source.AppendLine("using var callContext = Foundatio.Mediator.CallContext.Rent().Set(httpContext).Set(httpContext.Request).Set(httpContext.Response).Set(httpContext.User);");

                GenerateHandlerCall(source, handler, wrapperClassName, "message", isAsync, assemblySuffix);

                source.DecrementIndent();
                source.Append("}");
            }
        }
        else if (endpoint.SupportsAsParameters && hasAsParametersAttribute
            && (endpoint.RouteParameters.Length > 0 || endpoint.QueryParameters.Length > 0 || endpoint.BindingParameters.Length > 0))
        {
            // GET/DELETE with [AsParameters] - message type supports it and has bindable properties
            // ASP.NET natively respects [FromHeader]/[FromQuery]/[FromRoute] on the type's properties
            source.Append($"{asyncKeyword}([Microsoft.AspNetCore.Http.AsParameters] {messageType} message, ");
            source.Append("Microsoft.AspNetCore.Http.HttpContext httpContext, Foundatio.Mediator.IMediator mediator, System.Threading.CancellationToken cancellationToken) =>");
            source.AppendLine();
            source.AppendLine("{");
            source.IncrementIndent();

            source.AppendLine("using var callContext = Foundatio.Mediator.CallContext.Rent().Set(httpContext).Set(httpContext.Request).Set(httpContext.Response).Set(httpContext.User);");

            GenerateHandlerCall(source, handler, wrapperClassName, "message", isAsync, assemblySuffix);

            source.DecrementIndent();
            source.Append("}");
        }
        else
        {
            // GET/DELETE with constructor binding - need to construct the message
            var routeParams = endpoint.RouteParameters;
            var queryParams = endpoint.QueryParameters;
            var bindingParams = endpoint.BindingParameters;
            var allParams = routeParams.Concat(queryParams).Concat(bindingParams).ToList();

            source.Append($"{asyncKeyword}(");

            // Add route, query, and binding parameters
            for (int i = 0; i < allParams.Count; i++)
            {
                var param = allParams[i];
                var paramType = param.Type.FullName;

                if (param.BindingAttributeSyntax != null)
                {
                    // Use the explicit binding attribute (e.g., [FromHeader(Name = "X-Tenant-Id")])
                    source.Append($"{param.BindingAttributeSyntax} ");
                }
                else if (!param.IsRouteParameter)
                {
                    // Default: query parameters get [FromQuery]
                    source.Append("[Microsoft.AspNetCore.Mvc.FromQuery] ");
                }

                source.Append($"{paramType} {param.Name}");

                if (i < allParams.Count - 1)
                    source.Append(", ");
            }

            if (allParams.Count > 0)
                source.Append(", ");

            source.Append("Microsoft.AspNetCore.Http.HttpContext httpContext, Foundatio.Mediator.IMediator mediator, System.Threading.CancellationToken cancellationToken) =>");
            source.AppendLine();
            source.AppendLine("{");
            source.IncrementIndent();

            source.AppendLine("using var callContext = Foundatio.Mediator.CallContext.Rent().Set(httpContext).Set(httpContext.Request).Set(httpContext.Response).Set(httpContext.User);");

            // Construct the message from parameters
            if (allParams.Count > 0)
            {
                if (handler.MessageType.IsRecord)
                {
                    source.Append($"var message = new {messageType}(");
                    source.Append(string.Join(", ", allParams.Select(p => $"{p.PropertyName}: {p.Name}")));
                    source.AppendLine(");");
                }
                else
                {
                    source.Append($"var message = new {messageType} {{ ");
                    source.Append(string.Join(", ", allParams.Select(p => $"{p.PropertyName} = {p.Name}")));
                    source.AppendLine(" };");
                }
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
            source.AppendLine($"{awaitKeyword}global::Foundatio.Mediator.Generated.{wrapperClassName}.{handlerMethodName}(mediator, {messageVar}, callContext, cancellationToken);");
            source.AppendLine("return Microsoft.AspNetCore.Http.Results.Ok();");
        }
        else if (handler.ReturnType.IsFileResult)
        {
            source.AppendLine($"var result = {awaitKeyword}global::Foundatio.Mediator.Generated.{wrapperClassName}.{handlerMethodName}(mediator, {messageVar}, callContext, cancellationToken);");
            source.AppendLine($"return MediatorEndpointResultMapper_{assemblySuffix}.ToHttpResult(result);");
        }
        else if (handler.ReturnType.IsResult)
        {
            source.AppendLine($"var result = {awaitKeyword}global::Foundatio.Mediator.Generated.{wrapperClassName}.{handlerMethodName}(mediator, {messageVar}, callContext, cancellationToken);");
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
            source.AppendLine($"var stream = {awaitKeyword}global::Foundatio.Mediator.Generated.{wrapperClassName}.{handlerMethodName}(mediator, {messageVar}, callContext, cancellationToken);");
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
            source.AppendLine($"var result = {awaitKeyword}global::Foundatio.Mediator.Generated.{wrapperClassName}.{handlerMethodName}(mediator, {messageVar}, callContext, cancellationToken);");
            source.AppendLine("return Microsoft.AspNetCore.Http.Results.Ok(result);");
        }
    }

    /// <summary>
    /// Generates the MediatorEndpointResultMapper class.
    /// </summary>
    private static void GenerateResultMapperClass(IndentedStringBuilder source, string assemblySuffix)
    {
        source.AddGeneratedCodeAttribute();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine($"public static class MediatorEndpointResultMapper_{assemblySuffix}");
        source.AppendLine("{");
        source.IncrementIndent();

        source.AppendLine("/// <summary>");
        source.AppendLine("/// Converts a Foundatio.Mediator.IResult to an HTTP result.");
        source.AppendLine("/// </summary>");
        source.AppendLine("public static Microsoft.AspNetCore.Http.IResult ToHttpResult(Foundatio.Mediator.IResult result)");
        source.AppendLine("{");
        source.IncrementIndent();

        source.AppendLine("""
            return result.Status switch
            {
                Foundatio.Mediator.ResultStatus.Ok => result.GetValue() switch
                {
                    Foundatio.Mediator.FileResult file => Microsoft.AspNetCore.Http.Results.File(
                        file.Stream, file.ContentType, file.FileName),
                    { } v => Microsoft.AspNetCore.Http.Results.Ok(v),
                    _ => Microsoft.AspNetCore.Http.Results.Ok()
                },
                Foundatio.Mediator.ResultStatus.Created => Microsoft.AspNetCore.Http.Results.Created(
                    result.Location ?? "", result.GetValue()),
                Foundatio.Mediator.ResultStatus.Accepted => Microsoft.AspNetCore.Http.Results.Accepted(
                    string.IsNullOrEmpty(result.Location) ? null : result.Location, result.GetValue()),
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
            """);

        source.DecrementIndent();
        source.AppendLine("}");

        source.DecrementIndent();
        source.AppendLine("}");
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

        source.AppendLine();
        source.AddGeneratedCodeAttribute();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine("public static class MediatorEndpointExtensions");
        source.AppendLine("{");
        source.IncrementIndent();

        source.AppendLine("/// <summary>");
        source.AppendLine("/// Maps all discovered mediator handler endpoints from all referenced assemblies.");
        source.AppendLine("/// Discovers endpoint modules automatically via <see cref=\"FoundatioModuleAttribute\"/> and naming convention.");
        source.AppendLine("/// </summary>");
        source.AppendLine("/// <param name=\"endpoints\">The endpoint route builder.</param>");
        source.AppendLine("/// <param name=\"configure\">Optional configuration to select assemblies and enable logging.</param>");
        source.AppendLine("/// <returns>The endpoint route builder for chaining.</returns>");
        source.AppendLine("public static IEndpointRouteBuilder MapMediatorEndpoints(this IEndpointRouteBuilder endpoints, Action<MediatorEndpointOptionsBuilder>? configure = null)");
        source.AppendLine("{");
        source.IncrementIndent();

        source.AppendLine("MediatorEndpointOptions? options = null;");
        source.AppendLine("if (configure != null)");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine("var builder = new MediatorEndpointOptionsBuilder();");
        source.AppendLine("configure(builder);");
        source.AppendLine("options = builder.Build();");
        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("var logEndpoints = options?.LogEndpoints ?? false;");
        source.AppendLine();
        source.AppendLine("if (options?.Assemblies == null || options.Assemblies.Count == 0)");
        source.AppendLine("    MediatorExtensions.EnsureReferencedAssembliesLoaded();");
        source.AppendLine();
        source.AppendLine("var assemblies = options?.Assemblies != null && options.Assemblies.Count > 0");
        source.AppendLine("    ? options.Assemblies");
        source.AppendLine("    : AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).ToList();");
        source.AppendLine();
        source.AppendLine("foreach (var assembly in assemblies)");
        source.AppendLine("{");
        source.IncrementIndent();

        source.AppendLine("if (!assembly.GetCustomAttributes(typeof(FoundatioModuleAttribute), false).Any())");
        source.AppendLine("    continue;");
        source.AppendLine();
        source.AppendLine("foreach (var type in assembly.GetExportedTypes())");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine("if (!type.Name.EndsWith(\"_MediatorEndpoints\", StringComparison.Ordinal))");
        source.AppendLine("    continue;");
        source.AppendLine();
        source.AppendLine("var method = type.GetMethod(\"MapEndpoints\", BindingFlags.Public | BindingFlags.Static);");
        source.AppendLine("method?.Invoke(null, new object[] { endpoints, logEndpoints });");
        source.DecrementIndent();
        source.AppendLine("}");

        source.DecrementIndent();
        source.AppendLine("}");

        source.AppendLine("return endpoints;");
        source.DecrementIndent();
        source.AppendLine("}");

        source.DecrementIndent();
        source.AppendLine("}");

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

    /// <summary>
    /// Sanitizes a fully-qualified type name into a valid C# identifier
    /// by replacing non-identifier characters with underscores.
    /// </summary>
    private static string SanitizeIdentifier(string qualifiedName)
    {
        var sb = new System.Text.StringBuilder(qualifiedName.Length);
        foreach (var ch in qualifiedName)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }
        return sb.ToString();
    }
}
