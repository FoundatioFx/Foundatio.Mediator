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
        GeneratorConfiguration configuration,
        Compilation compilation)
    {
        // Check if the compilation supports minimal APIs
        if (!SupportsMinimalApis(compilation))
            return;

        // Filter handlers that should generate endpoints based on discovery mode
        var endpointHandlers = GetEndpointHandlers(handlers, configuration);

        if (endpointHandlers.Count == 0)
            return;

        // Generate the endpoint registration code
        var source = GenerateEndpointCode(endpointHandlers, configuration, compilation);
        context.AddSource("_MediatorEndpoints.g.cs", source);
    }

    /// <summary>
    /// Checks if the compilation references ASP.NET Core minimal APIs.
    /// </summary>
    private static bool SupportsMinimalApis(Compilation compilation)
    {
        // Check for IEndpointRouteBuilder which is the key type for minimal APIs
        var endpointRouteBuilder = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder");
        return endpointRouteBuilder != null;
    }

    /// <summary>
    /// Filters handlers based on endpoint discovery mode.
    /// </summary>
    private static List<HandlerInfo> GetEndpointHandlers(List<HandlerInfo> handlers, GeneratorConfiguration configuration)
    {
        return configuration.EndpointDiscoveryMode switch
        {
            "Explicit" => handlers
                .Where(h => h.Endpoint?.GenerateEndpoint == true)
                .ToList(),
            _ => handlers // "All" mode - include all handlers with endpoint info unless excluded
                .Where(h => h.Endpoint != null && h.Endpoint.Value.GenerateEndpoint)
                .ToList()
        };
    }

    /// <summary>
    /// Generates the complete endpoint registration source code.
    /// </summary>
    private static string GenerateEndpointCode(List<HandlerInfo> handlers, GeneratorConfiguration configuration, Compilation compilation)
    {
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, "_MediatorEndpoints.g.cs");

        // Check for available ASP.NET Core features
        bool hasAsParametersAttribute = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.AsParametersAttribute") != null;
        bool hasFromBodyAttribute = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromBodyAttribute") != null;
        bool hasWithOpenApi = HasWithOpenApiExtension(compilation);

        // Get suffix from configuration or fall back to assembly name
        var safeSuffix = !string.IsNullOrEmpty(configuration.ProjectName)
            ? configuration.ProjectName!.Replace(".", "_").Replace("-", "_").ToIdentifier()
            : (compilation.AssemblyName ?? "Unknown").Replace(".", "_").Replace("-", "_").ToIdentifier();

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
        source.AppendLine($"public static class MediatorEndpointExtensions_{safeSuffix}");
        source.AppendLine("{");
        source.IncrementIndent();

        // Generate MapMediatorEndpoints extension method with unique name
        GenerateMapMediatorEndpointsMethod(source, handlers, configuration, hasAsParametersAttribute, hasFromBodyAttribute, hasWithOpenApi, safeSuffix);

        source.DecrementIndent();
        source.AppendLine("}");
        source.AppendLine();

        // Generate the result mapper class (only once per assembly, uses same suffix)
        GenerateResultMapperClass(source, safeSuffix);

        return source.ToString();
    }

    /// <summary>
    /// Generates the MapMediatorEndpoints extension method.
    /// </summary>
    private static void GenerateMapMediatorEndpointsMethod(
        IndentedStringBuilder source,
        List<HandlerInfo> handlers,
        GeneratorConfiguration configuration,
        bool hasAsParametersAttribute,
        bool hasFromBodyAttribute,
        bool hasWithOpenApi,
        string assemblySuffix)
    {
        source.AppendLine("/// <summary>");
        source.AppendLine("/// Maps all discovered handler endpoints to the application.");
        source.AppendLine("/// </summary>");
        source.AppendLine("/// <param name=\"endpoints\">The endpoint route builder.</param>");
        source.AppendLine("/// <returns>The endpoint route builder for chaining.</returns>");
        source.AppendLine($"public static IEndpointRouteBuilder Map{assemblySuffix}Endpoints(this IEndpointRouteBuilder endpoints)");
        source.AppendLine("{");
        source.IncrementIndent();

        // Group handlers by category
        var handlersByCategory = handlers
            .GroupBy(h => h.Endpoint?.Category ?? "Default")
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var categoryGroup in handlersByCategory)
        {
            var category = categoryGroup.Key;
            var categoryHandlers = categoryGroup.ToList();

            // Get category route prefix from first handler
            var firstEndpoint = categoryHandlers.First().Endpoint!.Value;
            var routePrefix = firstEndpoint.CategoryRoutePrefix ?? "/api";

            source.AppendLine();
            source.AppendLine($"// {category} endpoints");

            // Create route group for the category
            var groupVarName = $"{ToCamelCase(category)}Group";
            var groupRequiresAuth = categoryHandlers.Any(h => h.Endpoint?.RequireAuth == true);

            source.Append($"var {groupVarName} = endpoints.MapGroup(\"{routePrefix}\")");

            // Only add tag if category is explicitly defined (not "Default")
            if (category != "Default")
            {
                source.Append($".WithTags(\"{category}\")");
            }

            // Add category-level auth if all handlers in category require auth
            var categoryRequireAuth = firstEndpoint.RequireAuth;
            if (categoryRequireAuth && !firstEndpoint.Policies.Any() && !firstEndpoint.Roles.Any())
            {
                source.Append(".RequireAuthorization()");
            }

            source.AppendLine(";");
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
                GenerateEndpoint(source, handler, groupVarName, hasAsParametersAttribute, hasFromBodyAttribute, hasWithOpenApi, categoryRequireAuth, assemblySuffix, routeOverride);
            }
        }

        source.AppendLine();
        source.AppendLine("return endpoints;");
        source.DecrementIndent();
        source.AppendLine("}");
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
                var kebabRoute = "/" + ToKebabCase(messageName);
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

        if (!string.IsNullOrEmpty(endpoint.Summary))
        {
            var escapedSummary = EscapeString(endpoint.Summary!);
            source.AppendLine($".WithSummary(\"{escapedSummary}\")");
        }

        if (!string.IsNullOrEmpty(endpoint.Description))
        {
            var escapedDescription = EscapeString(endpoint.Description!);
            source.AppendLine($".WithDescription(\"{escapedDescription}\")");
        }

        // Add endpoint-specific auth if different from category
        if (endpoint.RequireAuth && !categoryRequireAuth)
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
        else if (endpoint.Policies.Any())
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

        if (hasWithOpenApi)
        {
            source.AppendLine(".WithOpenApi()");
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
                source.Append(string.Join(", ", allParams.Select(p => p.Name)));
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
        else if (handler.ReturnType.IsResult)
        {
            source.AppendLine($"var result = {awaitKeyword}global::Foundatio.Mediator.Generated.{wrapperClassName}.{handlerMethodName}(mediator, {messageVar}, cancellationToken);");
            source.AppendLine($"return MediatorEndpointResultMapper_{assemblySuffix}.ToHttpResult(result);");
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
                Foundatio.Mediator.ResultStatus.Success => result.GetValue() is { } v
                    ? Microsoft.AspNetCore.Http.Results.Ok(v)
                    : Microsoft.AspNetCore.Http.Results.Ok(),
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
    /// Checks if the compilation has the WithOpenApi extension method.
    /// </summary>
    private static bool HasWithOpenApiExtension(Compilation compilation)
    {
        // Check for OpenApiRouteHandlerBuilderExtensions
        var extensionType = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Builder.OpenApiRouteHandlerBuilderExtensions");
        return extensionType != null;
    }

    /// <summary>
    /// Converts PascalCase to kebab-case. Also handles underscores by replacing them with dashes.
    /// </summary>
    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '_')
            {
                // Replace underscore with dash, but avoid double dashes
                if (result.Length > 0 && result[result.Length - 1] != '-')
                    result.Append('-');
            }
            else if (char.IsUpper(c))
            {
                if (result.Length > 0 && result[result.Length - 1] != '-')
                    result.Append('-');
                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// Converts PascalCase to camelCase.
    /// </summary>
    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (char.IsLower(value[0]))
            return value;

        return char.ToLowerInvariant(value[0]) + value.Substring(1);
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
