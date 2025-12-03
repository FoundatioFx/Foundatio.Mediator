using System.Collections.Generic;
using System.Text;
using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator;

internal static class EndpointGenerator
{
    private static readonly string[] SupportedPrefixes = ["Get", "Create", "Update", "Delete"];

    public static void Execute(SourceProductionContext context, List<HandlerInfo> handlers, Compilation compilation)
    {
        if (!SupportsMinimalApis(compilation))
            return;

        var endpointHandlers = handlers
            .Where(IsEndpointCandidate)
            .OrderBy(h => h.MessageType.FullName, StringComparer.Ordinal)
            .ToList();

        if (endpointHandlers.Count == 0)
            return;

        bool hasAsParameters = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.AsParametersAttribute") is not null
            || compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.AsParametersAttribute") is not null;
        bool hasFromBody = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromBodyAttribute") is not null;
        bool hasFromQuery = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromQueryAttribute") is not null;
        bool hasFromServices = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromServicesAttribute") is not null;
        bool hasOpenApi = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.OpenApi.OpenApiRouteHandlerBuilderExtensions") is not null;

        var source = BuildSource(endpointHandlers, hasAsParameters, hasFromBody, hasFromQuery, hasFromServices, hasOpenApi);
        context.AddSource("MediatorMinimalApiEndpoints.g.cs", source);
    }

    private static string BuildSource(IReadOnlyList<HandlerInfo> handlers, bool hasAsParameters, bool hasFromBody, bool hasFromQuery, bool hasFromServices, bool hasOpenApi)
    {
        var source = new IndentedStringBuilder();
        source.AddGeneratedFileHeader();

        source.AppendLines("""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            """);

        if (hasAsParameters || hasFromBody || hasFromQuery || hasFromServices)
            source.AppendLine("using Microsoft.AspNetCore.Mvc;");

        source.AppendLines("""
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.DependencyInjection;

            namespace Foundatio.Mediator;

            public static class MediatorEndpointExtensions
            {
                private const string DefaultBasePath = "/api/messages";

                public static IEndpointRouteBuilder MapMediatorEndpoints(this IEndpointRouteBuilder app, string? basePath = null)
                {
                    ArgumentNullException.ThrowIfNull(app);
                    var group = app.MapGroup(string.IsNullOrWhiteSpace(basePath) ? DefaultBasePath : basePath!);
                    RegisterMediatorEndpoints(group);
                    return app;
                }

                private static void RegisterMediatorEndpoints(RouteGroupBuilder group)
                {
                    ArgumentNullException.ThrowIfNull(group);
            """);

        source.IncrementIndent();
        source.IncrementIndent();

        var categoryOrder = new List<string>();
        var handlersByCategory = new Dictionary<string, List<HandlerInfo>>(StringComparer.Ordinal);
        var categoryLabels = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var handler in handlers)
        {
            var categoryKey = handler.Category?.Trim() ?? string.Empty;
            if (!handlersByCategory.TryGetValue(categoryKey, out var list))
            {
                list = new List<HandlerInfo>();
                handlersByCategory[categoryKey] = list;
                categoryOrder.Add(categoryKey);
                categoryLabels[categoryKey] = handler.Category?.Trim();
            }

            list.Add(handler);
        }

        int endpointIndex = 0;
        int categoryIndex = 0;

        for (int categoryOrderIndex = 0; categoryOrderIndex < categoryOrder.Count; categoryOrderIndex++)
        {
            var categoryKey = categoryOrder[categoryOrderIndex];
            var categoryHandlers = handlersByCategory[categoryKey];
            string? categoryLabel = categoryLabels[categoryKey];
            string groupAccessor = "group";

            if (!string.IsNullOrEmpty(categoryKey) && !string.IsNullOrWhiteSpace(categoryLabel))
            {
                string categoryVar = $"categoryGroup{categoryIndex++}";
                string routeSegment = ToCategoryRouteSegmentLiteral(categoryLabel!);
                source.AppendLine($"var {categoryVar} = group.MapGroup(\"/{routeSegment}\");");
                source.AppendLine($"{categoryVar}.WithGroupName(\"{EscapeString(categoryLabel!)}\");");
                source.AppendLine();
                groupAccessor = categoryVar;
            }

            foreach (var handler in categoryHandlers)
            {
                string handlerClassName = HandlerGenerator.GetHandlerClassName(handler);
                string methodName = HandlerGenerator.GetHandlerDefaultMethodName(handler);
                string mapMethod = GetMapMethod(handler);
                string routeSegment = ToRouteSegment(handler.MessageType.Name);
                string requestParameter = $"{handler.MessageType.FullName} request";
                bool handlerDefaultReturnsValue = HandlerGenerator.HandlerDefaultReturnsValue(handler);
                bool handlerDefaultIsAsync = HandlerGenerator.HandlerDefaultIsAsync(handler);

                bool requiresQueryBinding = RequiresQueryBinding(mapMethod);
                bool requiresBodyBinding = RequiresBodyBinding(mapMethod);

                if (requiresQueryBinding)
                {
                    if (hasAsParameters)
                    {
                        requestParameter = "[AsParameters] " + requestParameter;
                    }
                    else if (hasFromQuery)
                    {
                        requestParameter = "[FromQuery] " + requestParameter;
                    }
                }
                else if (requiresBodyBinding && hasFromBody)
                {
                    requestParameter = "[FromBody] " + requestParameter;
                }

                string servicesParameter = hasFromServices ? "[FromServices] IServiceProvider services" : "IServiceProvider services";
                string mediatorParameter = hasFromServices ? "[FromServices] Foundatio.Mediator.IMediator mediator" : "Foundatio.Mediator.IMediator mediator";

                string invocation = handlerDefaultIsAsync
                    ? $"await {handlerClassName}.{methodName}(mediator, services, request, cancellationToken).ConfigureAwait(false)"
                    : $"{handlerClassName}.{methodName}(mediator, services, request, cancellationToken)";

                string endpointVar = $"endpoint{endpointIndex++}";

                source.AppendLine($"var {endpointVar} = {groupAccessor}.{mapMethod}(\"/{routeSegment}\", async ({requestParameter}, {servicesParameter}, {mediatorParameter}, CancellationToken cancellationToken) =>");
                source.AppendLine("{");
                source.IncrementIndent();
                if (handlerDefaultReturnsValue)
                {
                    source.AppendLine($"var handlerResult = {invocation};");
                    source.AppendLine("return MediatorEndpointResultMapper.ToHttpResult(handlerResult);");
                }
                else
                {
                    source.AppendLine($"{invocation};");
                    source.AppendLine("return MediatorEndpointResultMapper.ToHttpResult(null);");
                }
                source.DecrementIndent();
                source.AppendLine("});");
                source.AppendLine($"{endpointVar}.WithName(\"{handler.MessageType.Name}\");");
                if (!string.IsNullOrWhiteSpace(handler.MessageSummary))
                {
                    var escapedSummary = EscapeString(handler.MessageSummary!);
                    source.AppendLine($"{endpointVar}.WithSummary(\"{escapedSummary}\");");
                    source.AppendLine($"{endpointVar}.WithDescription(\"{escapedSummary}\");");
                }
                if (hasOpenApi)
                    source.AppendLine($"{endpointVar}.WithOpenApi();");
                source.AppendLine();
            }

            if (categoryOrderIndex < categoryOrder.Count - 1)
                source.AppendLine();
        }

        source.DecrementIndent();
        source.DecrementIndent();

        source.AppendLines("""
                }

                private static bool RequiresQueryBinding(string mapMethod) => mapMethod is "MapGet" or "MapDelete";
                private static bool RequiresBodyBinding(string mapMethod) => mapMethod is "MapPost" or "MapPut";

                private static class MediatorEndpointResultMapper
                {
                    public static global::Microsoft.AspNetCore.Http.IResult ToHttpResult(object? value)
                    {
                        if (value is null)
                            return Results.NoContent();

                        if (value is Foundatio.Mediator.IResult mediatorResult)
                            return MapMediatorResult(mediatorResult);

                        return Results.Ok(value);
                    }

                    private static global::Microsoft.AspNetCore.Http.IResult MapMediatorResult(Foundatio.Mediator.IResult result)
                    {
                        return result.Status switch
                        {
                            ResultStatus.Success => result.GetValue() is { } value ? Results.Ok(value) : Results.Ok(),
                            ResultStatus.Created => Results.Created(string.IsNullOrWhiteSpace(result.Location) ? DefaultBasePath : result.Location, result.GetValue()),
                            ResultStatus.NoContent => Results.NoContent(),
                            ResultStatus.BadRequest => Results.BadRequest(result.Message),
                            ResultStatus.Invalid => Results.ValidationProblem(ToValidationState(result.ValidationErrors), detail: result.Message),
                            ResultStatus.NotFound => string.IsNullOrWhiteSpace(result.Message) ? Results.NotFound() : Results.NotFound(result.Message),
                            ResultStatus.Unauthorized => Results.Unauthorized(),
                            ResultStatus.Forbidden => Results.Forbid(),
                            ResultStatus.Conflict => Results.Conflict(result.Message),
                            ResultStatus.Error => Results.Problem(detail: result.Message),
                            ResultStatus.CriticalError => Results.Problem(detail: result.Message, statusCode: StatusCodes.Status500InternalServerError),
                            ResultStatus.Unavailable => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
                            _ => Results.Ok(result.GetValue())
                        };
                    }

                    private static IDictionary<string, string[]> ToValidationState(IEnumerable<ValidationError> errors)
                    {
                        var dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                        foreach (var error in errors)
                        {
                            var key = string.IsNullOrWhiteSpace(error.Identifier) ? string.Empty : error.Identifier;
                            if (!dictionary.TryGetValue(key, out var list))
                            {
                                list = new List<string>();
                                dictionary[key] = list;
                            }

                            if (!string.IsNullOrWhiteSpace(error.ErrorMessage))
                                list.Add(error.ErrorMessage);
                        }

                        return dictionary.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
                    }
                }
            }
            """);

        return source.ToString();
    }

    private static bool SupportsMinimalApis(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder") is not null
            && compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Http.Results") is not null;
    }

    private static bool IsEndpointCandidate(HandlerInfo handler)
    {
        foreach (var prefix in SupportedPrefixes)
        {
            if (handler.MessageType.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string GetMapMethod(HandlerInfo handler)
    {
        var name = handler.MessageType.Name;
        if (name.StartsWith("Get", StringComparison.OrdinalIgnoreCase))
            return "MapGet";
        if (name.StartsWith("Update", StringComparison.OrdinalIgnoreCase))
            return "MapPut";
        if (name.StartsWith("Create", StringComparison.OrdinalIgnoreCase))
            return "MapPost";
        if (name.StartsWith("Delete", StringComparison.OrdinalIgnoreCase))
            return "MapDelete";

        return "MapPost";
    }

    private static string ToRouteSegment(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var builder = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0)
                builder.Append('-');

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static string EscapeString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    private static string ToCategoryRouteSegmentLiteral(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return "uncategorized";

        var builder = new StringBuilder();
        foreach (var c in category)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                continue;
            }

            if (builder.Length == 0 || builder[builder.Length - 1] == '-')
                continue;

            builder.Append('-');
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "uncategorized" : result;
    }

    private static bool RequiresQueryBinding(string mapMethod) => mapMethod is "MapGet" or "MapDelete";
    private static bool RequiresBodyBinding(string mapMethod) => mapMethod is "MapPost" or "MapPut";
}
