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

        var safeSuffix = !string.IsNullOrEmpty(configuration.ProjectName)
            ? configuration.ProjectName!.Replace(".", "_").Replace("-", "_").ToIdentifier()
            : DeriveProjectNameFromAssembly(compilationInfo.AssemblyName);

        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, "_MediatorEndpoints.Api.g.cs");
        source.AppendLine("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using System.Diagnostics.CodeAnalysis;

            namespace Foundatio.Mediator;
            """);

        source.AppendLine();
        source.AddGeneratedCodeAttribute();
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine($"public static partial class MediatorEndpointExtensions_{safeSuffix}");
        source.AppendLine("{");
        source.IncrementIndent();

        source.AppendLine("/// <summary>");
        source.AppendLine("/// Maps all discovered handler endpoints to the application.");
        source.AppendLine("/// </summary>");
        source.AppendLine("/// <param name=\"endpoints\">The endpoint route builder.</param>");
        source.AppendLine("/// <param name=\"logEndpoints\">When true, logs all mapped endpoints at startup using ILogger or Console.</param>");
        source.AppendLine("/// <returns>The endpoint route builder for chaining.</returns>");
        source.AppendLine($"public static IEndpointRouteBuilder Map{safeSuffix}Endpoints(this IEndpointRouteBuilder endpoints, bool logEndpoints = false)");
        source.AppendLine("{");
        source.IncrementIndent();
        source.AppendLine("MapEndpointsCore(endpoints, logEndpoints);");
        source.AppendLine("return endpoints;");
        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("/// <summary>");
        source.AppendLine("/// Core endpoint registration, implemented by the source generator at compile time.");
        source.AppendLine("/// At design time this is a no-op so IntelliSense works before the first build.");
        source.AppendLine("/// </summary>");
        source.AppendLine("static partial void MapEndpointsCore(IEndpointRouteBuilder endpoints, bool logEndpoints);");

        source.DecrementIndent();
        source.AppendLine("}");

        context.AddSource("_MediatorEndpoints.Api.g.cs", source.ToString());
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

        if (endpointHandlers.Count == 0)
            return;

        // Validate endpoint configurations and emit diagnostics
        ValidateEndpoints(context, endpointHandlers, endpointDefaults);

        // Generate the endpoint registration code
        var source = GenerateEndpointCode(endpointHandlers, endpointDefaults, configuration, compilationInfo);
        context.AddSource("_MediatorEndpoints.g.cs", source);
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
        var warnedCategories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var handler in handlers)
        {
            var endpoint = handler.Endpoint!.Value;

            // FMED015: Category route prefix duplicates global endpoint prefix.
            // Only applies to relative prefixes (no leading /) since absolute prefixes bypass the global group.
            var globalPrefix = endpointDefaults.RoutePrefix;
            var catPrefix = endpoint.CategoryRoutePrefix;
            if (!endpoint.CategoryBypassGlobalPrefix
                && !string.IsNullOrEmpty(globalPrefix)
                && !string.IsNullOrEmpty(catPrefix))
            {
                // For relative prefixes, check if the prefix content duplicates the global prefix content.
                // e.g. global = "/api", relative category = "api/products" → /api/api/products (wrong)
                var globalContent = globalPrefix!.TrimStart('/');
                var catContent = catPrefix!.TrimStart('/');
                if (catContent.StartsWith(globalContent, StringComparison.OrdinalIgnoreCase)
                    && catContent.Length > globalContent.Length
                    && warnedCategories.Add(catPrefix))
                {
                    var suggested = catContent.Substring(globalContent.Length).TrimStart('/');
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "FMED015",
                            "Category route prefix duplicates global endpoint prefix",
                            "HandlerCategory RoutePrefix '{0}' starts with the global EndpointRoutePrefix '{1}' content, which will produce a doubled path. " +
                            "Remove the duplicated portion (e.g. use '{2}' instead), or prefix with '/' for an absolute path that bypasses the global prefix.",
                            "Foundatio.Mediator",
                            DiagnosticSeverity.Warning,
                            isEnabledByDefault: true),
                        Location.None,
                        catPrefix,
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
    }

    /// <summary>
    /// Generates the complete endpoint registration source code.
    /// </summary>
    private static string GenerateEndpointCode(List<HandlerInfo> handlers, EndpointDefaultsInfo endpointDefaults, GeneratorConfiguration configuration, CompilationInfo compilationInfo)
    {
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, "_MediatorEndpoints.g.cs");

        // Check for available ASP.NET Core features
        bool hasAsParametersAttribute = compilationInfo.HasAsParametersAttribute;
        bool hasFromBodyAttribute = compilationInfo.HasFromBodyAttribute;
        bool hasWithOpenApi = compilationInfo.HasWithOpenApi;

        // Get suffix from configuration or fall back to a cleaned-up assembly name
        var safeSuffix = !string.IsNullOrEmpty(configuration.ProjectName)
            ? configuration.ProjectName!.Replace(".", "_").Replace("-", "_").ToIdentifier()
            : DeriveProjectNameFromAssembly(compilationInfo.AssemblyName);

        // Check if any handler uses SSE streaming
        bool hasAnySseEndpoints = handlers.Any(h => h.Endpoint is { IsStreaming: true, StreamingFormat: "ServerSentEvents" });

        source.AppendLine("""
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using System.Diagnostics.CodeAnalysis;
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
        source.AppendLine($"namespace Foundatio.Mediator;");

        source.AppendLine();
        source.AppendLine($"public static partial class MediatorEndpointExtensions_{safeSuffix}");
        source.AppendLine("{");
        source.IncrementIndent();

        // Generate the core implementation as a static partial void method
        // that fills in the stub declared in _MediatorEndpoints.Api.g.cs
        GenerateMapMediatorEndpointsCoreMethod(source, handlers, endpointDefaults, configuration, hasAsParametersAttribute, hasFromBodyAttribute, hasWithOpenApi, safeSuffix, compilationInfo.HasLoggerFactory);

        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();

        // Generate the result mapper class (only once per assembly, uses same suffix)
        GenerateResultMapperClass(source, safeSuffix);

        return source.ToString();
    }

    /// <summary>
    /// Generates the MapEndpointsCore static partial void implementation.
    /// This provides the compile-time body for the stub declared in _MediatorEndpoints.Api.g.cs.
    /// </summary>
    private static void GenerateMapMediatorEndpointsCoreMethod(
        IndentedStringBuilder source,
        List<HandlerInfo> handlers,
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

        // Determine the parent variable name for category groups
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

        // Group handlers by category
        var handlersByCategory = handlers
            .GroupBy(h => h.Endpoint?.Category ?? "Default")
            .OrderBy(g => g.Key)
            .ToList();

        // Collect endpoint info for startup logging
        var endpointLogEntries = new List<(string HttpMethod, string FullRoute, string HandlerInfo)>();

        foreach (var categoryGroup in handlersByCategory)
        {
            var category = categoryGroup.Key;
            var categoryHandlers = categoryGroup.ToList();

            // Get category route prefix from first handler
            var firstEndpoint = categoryHandlers.First().Endpoint!.Value;
            var routePrefix = firstEndpoint.CategoryRoutePrefix ?? "";

            // When the category uses an absolute prefix (leading /), bypass the global route prefix
            var categoryParent = (firstEndpoint.CategoryBypassGlobalPrefix && hasGlobalGroup)
                ? "endpoints"
                : parentGroupVar;

            source.AppendLine();
            source.AppendLine($"// {category} endpoints");

            // Create route group for the category
            var groupVarName = $"{category.ToCamelCase()}Group";

            source.Append($"var {groupVarName} = {categoryParent}.MapGroup(\"{routePrefix}\")");

            // Only add tag if category is explicitly defined (not "Default")
            if (category != "Default")
            {
                source.Append($".WithTags(\"{category}\")");
            }

            // Add category-level auth if the category requires auth (and global doesn't already)
            var categoryRequireAuth = firstEndpoint.RequireAuth && !endpointDefaults.RequireAuth;
            if (categoryRequireAuth && !firstEndpoint.Policies.Any() && !firstEndpoint.Roles.Any())
            {
                source.Append(".RequireAuthorization()");
            }

            source.AppendLine(";");

            // Apply category-level filters
            var categoryFilters = firstEndpoint.CategoryFilters;
            foreach (var filter in categoryFilters)
            {
                source.AppendLine($"{groupVarName}.AddEndpointFilter<{filter}>();");
            }

            source.AppendLine();

            // Detect duplicate routes within this category and resolve conflicts
            var routeOverrides = ResolveDuplicateRoutes(categoryHandlers);

            // Generate endpoint for each handler in the category
            foreach (var handler in categoryHandlers)
            {
                // Check if this handler needs a route override due to conflict
                // Use the unique handler key that includes message type
                var handlerKey = HandlerGenerator.GetHandlerClassName(handler);
                routeOverrides.TryGetValue(handlerKey, out var routeOverride);

                // When the explicit route is absolute (leading /), bypass all prefixes
                var targetGroup = handler.Endpoint!.Value.RouteBypassPrefixes
                    ? "endpoints"
                    : groupVarName;

                GenerateEndpoint(source, handler, targetGroup, hasAsParametersAttribute, hasFromBodyAttribute, hasWithOpenApi, categoryRequireAuth || endpointDefaults.RequireAuth, assemblySuffix, endpointDefaults.SummaryStyle, routeOverride);

                // Collect endpoint info for logging
                var endpointRoute = routeOverride ?? handler.Endpoint!.Value.Route;
                var fullRoute = ComputeFullDisplayRoute(
                    endpointDefaults.RoutePrefix, routePrefix, endpointRoute,
                    firstEndpoint.CategoryBypassGlobalPrefix,
                    handler.Endpoint!.Value.RouteBypassPrefixes);
                endpointLogEntries.Add((
                    handler.Endpoint!.Value.HttpMethod,
                    fullRoute,
                    $"{handler.Identifier}.{handler.MethodName}({handler.MessageType.Identifier})"));
            }
        }

        // Emit endpoint logging block
        EmitEndpointLogging(source, endpointLogEntries, hasLoggerFactory);

        source.DecrementIndent();
        source.AppendLine("}");
    }

    /// <summary>
    /// Emits the endpoint logging block at the end of MapEndpointsCore.
    /// </summary>
    private static void EmitEndpointLogging(
        IndentedStringBuilder source,
        List<(string HttpMethod, string FullRoute, string HandlerInfo)> entries,
        bool hasLoggerFactory)
    {
        if (entries.Count == 0)
            return;

        var maxMethodLen = entries.Max(e => e.HttpMethod.Length);
        var maxRouteLen = entries.Max(e => e.FullRoute.Length);

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

        foreach (var (httpMethod, fullRoute, handlerInfo) in entries)
        {
            var paddedMethod = httpMethod.PadRight(maxMethodLen);
            var paddedRoute = fullRoute.PadRight(maxRouteLen);
            source.AppendLine($"writeLog(\"  {paddedMethod}  {paddedRoute}  \u2192 {handlerInfo}\");");
        }

        source.DecrementIndent();
        source.AppendLine("}");
    }

    /// <summary>
    /// Computes the full display route path for logging by combining global prefix, category prefix, and endpoint route.
    /// </summary>
    private static string ComputeFullDisplayRoute(string? globalPrefix, string categoryPrefix, string endpointRoute, bool categoryBypassGlobalPrefix, bool routeBypassPrefixes)
    {
        string result;
        if (routeBypassPrefixes)
            result = endpointRoute;
        else if (categoryBypassGlobalPrefix)
            result = JoinRouteParts(categoryPrefix, endpointRoute);
        else
            result = JoinRouteParts(globalPrefix ?? "", JoinRouteParts(categoryPrefix, endpointRoute));

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
    /// Detects duplicate routes within a category and returns overrides for conflicting handlers.
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
        bool categoryRequireAuth,
        string assemblySuffix,
        string summaryStyle,
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

        // Add endpoint-specific auth if different from category
        if (!endpoint.AllowAnonymous && endpoint.RequireAuth && !categoryRequireAuth)
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
    internal static string DeriveProjectNameFromAssembly(string assemblyName)
        => AssemblyNameHelper.DeriveProjectNameFromAssembly(assemblyName);

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
