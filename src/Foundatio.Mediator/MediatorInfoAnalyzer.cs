using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace Foundatio.Mediator;

/// <summary>
/// Real-time Roslyn analyzer that shows Info-level diagnostics on handler methods and message types.
/// FMED017: Shows the generated endpoint route on handler methods.
/// FMED018: Shows the handler location on message type declarations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MediatorInfoAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor EndpointRouteInfo = new(
        id: "FMED017",
        title: "Endpoint route info",
        messageFormat: "Endpoint: {0} {1}",
        category: "Foundatio.Mediator",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: "https://foundatio-mediator.dev/guide/endpoints.html");

    private static readonly DiagnosticDescriptor HandlerLocationInfo = new(
        id: "FMED018",
        title: "Handler info",
        messageFormat: "Handled by {0}.{1}({2}) in {3}",
        category: "Foundatio.Mediator",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: "https://foundatio-mediator.dev/guide/handler-conventions.html");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(EndpointRouteInfo, HandlerLocationInfo);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationStart =>
        {
            var compilation = compilationStart.Compilation;

            // Read assembly-level configuration once per compilation
            var config = ReadEndpointConfig(compilation);

            // If endpoints are disabled entirely, only register FMED018
            if (config.Discovery == "None")
            {
                compilationStart.RegisterSymbolAction(
                    ctx => AnalyzeMessageType(ctx, compilation),
                    SymbolKind.NamedType);
                return;
            }

            compilationStart.RegisterSymbolAction(ctx =>
            {
                if (ctx.Symbol is IMethodSymbol method)
                    AnalyzeHandlerMethod(ctx, method, compilation, config);
            }, SymbolKind.Method);

            compilationStart.RegisterSymbolAction(
                ctx => AnalyzeMessageType(ctx, compilation),
                SymbolKind.NamedType);
        });
    }

    /// <summary>
    /// FMED017: On a handler method, compute and report the endpoint route.
    /// </summary>
    private static void AnalyzeHandlerMethod(
        SymbolAnalysisContext ctx,
        IMethodSymbol method,
        Compilation compilation,
        EndpointConfig config)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
            return;

        var containingType = method.ContainingType;
        if (containingType == null || containingType.TypeKind != TypeKind.Class)
            return;

        // Skip generated types
        if (containingType.ContainingNamespace?.ToDisplayString() == WellKnownTypes.GeneratedNamespace)
            return;

        // Skip ignored types
        if (containingType.HasIgnoreAttribute(compilation) || method.HasIgnoreAttribute(compilation))
            return;

        // Determine if this is a handler class (by naming convention, interface, or attribute)
        bool isHandlerClass = IsHandlerClass(containingType, compilation);

        // Check if this method is a handler method
        if (!IsHandlerMethod(method, compilation, isHandlerClass))
            return;

        // Get the message type (first parameter)
        if (method.Parameters.Length == 0)
            return;

        var messageType = method.Parameters[0].Type as INamedTypeSymbol;
        if (messageType == null)
            return;

        // Check if this handler generates an endpoint
        // Skip events/notifications
        if (IsEventType(containingType, messageType))
            return;

        // Check for explicit exclusion via [HandlerEndpoint(Exclude = true)]
        if (IsEndpointExcluded(method, containingType))
            return;

        // If discovery is "Explicit", only show route when [HandlerEndpoint] is present
        if (config.Discovery == "Explicit")
        {
            if (!HasEndpointAttribute(method) && !HasEndpointAttribute(containingType))
                return;
        }

        // Compute the route
        var route = ComputeEndpointRoute(method, containingType, messageType, compilation, config);
        if (route == null)
            return;

        // Get the method's location for the diagnostic
        var location = method.Locations.FirstOrDefault();
        if (location == null)
            return;

        // Narrow to just the method identifier
        var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef?.GetSyntax() is MethodDeclarationSyntax methodSyntax)
            location = methodSyntax.Identifier.GetLocation();

        ctx.ReportDiagnostic(Diagnostic.Create(
            EndpointRouteInfo,
            location,
            route.Value.HttpMethod,
            route.Value.FullRoute));
    }

    /// <summary>
    /// FMED018: On a message type declaration, report which handler handles it.
    /// </summary>
    private static void AnalyzeMessageType(
        SymbolAnalysisContext ctx,
        Compilation compilation)
    {
        if (ctx.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        // Only analyze records/classes that could be messages (skip interfaces, enums, etc.)
        if (typeSymbol.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return;

        // Skip types in generated namespace
        if (typeSymbol.ContainingNamespace?.ToDisplayString() == WellKnownTypes.GeneratedNamespace)
            return;

        // Skip types with handler/middleware naming — they are handlers, not messages
        var name = typeSymbol.Name;
        if (name.EndsWith("Handler") || name.EndsWith("Consumer") || name.EndsWith("Middleware"))
            return;

        // Search for handlers in the same compilation that handle this message type
        var handler = FindHandlerForMessage(typeSymbol, compilation);
        if (handler == null)
            return;

        // Get the message type declaration location
        var location = typeSymbol.Locations.FirstOrDefault();
        if (location == null || !location.IsInSource)
            return;

        // Narrow to just the type identifier
        var syntaxRef = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef != null)
        {
            var syntax = syntaxRef.GetSyntax();
            if (syntax is TypeDeclarationSyntax typeDeclSyntax)
                location = typeDeclSyntax.Identifier.GetLocation();
            else if (syntax is RecordDeclarationSyntax recordSyntax)
                location = recordSyntax.Identifier.GetLocation();
        }

        // Add the handler's location as an additional location for "go to" navigation
        var handlerLocation = handler.Value.Method.Locations.FirstOrDefault();
        var additionalLocations = handlerLocation != null && handlerLocation.IsInSource
            ? ImmutableArray.Create(handlerLocation)
            : ImmutableArray<Location>.Empty;

        ctx.ReportDiagnostic(Diagnostic.Create(
            HandlerLocationInfo,
            location,
            additionalLocations,
            properties: null,
            handler.Value.ClassName,
            handler.Value.MethodName,
            handler.Value.MessageTypeName,
            handler.Value.FileName));
    }

    #region Handler Discovery

    private static bool IsHandlerClass(INamedTypeSymbol type, Compilation compilation)
    {
        var typeName = type.Name;

        if (typeName.EndsWith("Handler") || typeName.EndsWith("Consumer"))
            return true;

        if (type.AllInterfaces.Any(i => i.ToDisplayString() == WellKnownTypes.IHandler))
            return true;

        if (type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute))
            return true;

        return false;
    }

    private static bool IsHandlerMethod(IMethodSymbol method, Compilation compilation, bool isHandlerClass)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
            return false;

        bool hasMethodHandlerAttribute = method.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute);

        if (!isHandlerClass && !hasMethodHandlerAttribute)
            return false;

        if (isHandlerClass && !SymbolUtilities.ValidHandlerMethodNames.Contains(method.Name))
            return false;

        if (method.HasIgnoreAttribute(compilation))
            return false;

        if (method.IsMassTransitConsumeMethod())
            return false;

        return true;
    }

    #endregion

    #region Event Detection

    private static bool IsEventType(INamedTypeSymbol handlerClass, INamedTypeSymbol messageType)
    {
        // Event naming conventions
        var msgName = messageType.Name;
        if (msgName.EndsWith("Event") || msgName.EndsWith("Notification") || msgName.EndsWith("Published"))
            return true;

        // Handler class named *Consumer typically handles events
        if (handlerClass.Name.EndsWith("Consumer"))
            return true;

        return false;
    }

    #endregion

    #region Endpoint Attribute Checks

    private static bool IsEndpointExcluded(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        var methodAttr = method.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerEndpointAttribute);
        var classAttr = containingType.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerEndpointAttribute);

        return GetBoolProperty(methodAttr, "Exclude") ?? GetBoolProperty(classAttr, "Exclude") ?? false;
    }

    private static bool HasEndpointAttribute(ISymbol symbol) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerEndpointAttribute);

    #endregion

    #region Route Computation

    private readonly record struct RouteResult(string HttpMethod, string FullRoute);

    private static RouteResult? ComputeEndpointRoute(
        IMethodSymbol method,
        INamedTypeSymbol containingType,
        INamedTypeSymbol messageType,
        Compilation compilation,
        EndpointConfig config)
    {
        // Read [HandlerEndpoint] attributes
        var methodEndpointAttr = method.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerEndpointAttribute);
        var classEndpointAttr = containingType.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerEndpointAttribute);

        // Read [HandlerEndpointGroup] attribute
        var groupAttr = containingType.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerEndpointGroupAttribute);

        // HTTP method
        var httpMethodEnum = GetIntProperty(methodEndpointAttr, "HttpMethod")
                          ?? GetIntProperty(classEndpointAttr, "HttpMethod")
                          ?? 0;
        var httpMethod = httpMethodEnum switch
        {
            1 => "GET",
            2 => "POST",
            3 => "PUT",
            4 => "DELETE",
            5 => "PATCH",
            _ => RouteConventions.InferHttpMethod(messageType.Name)
        };

        // Group info from [HandlerEndpointGroup]
        string? groupName = null;
        string? groupRoutePrefix = null;
        if (groupAttr != null)
        {
            if (groupAttr.ConstructorArguments.Length > 0)
                groupName = groupAttr.ConstructorArguments[0].Value as string;
            groupName ??= GetStringProperty(groupAttr, "Name");

            // Auto-derive group name from handler class when not specified
            if (string.IsNullOrEmpty(groupName))
            {
                var prefix = RouteConventions.GetHandlerPrefix(containingType.Name);
                if (!string.IsNullOrEmpty(prefix))
                    groupName = prefix;
            }

            groupRoutePrefix = GetStringProperty(groupAttr, "RoutePrefix");
            if (string.IsNullOrEmpty(groupRoutePrefix) && !string.IsNullOrEmpty(groupName))
                groupRoutePrefix = groupName!.ToKebabCase();
        }

        bool groupBypassGlobalPrefix = false;
        if (!string.IsNullOrEmpty(groupRoutePrefix) && groupRoutePrefix!.StartsWith("/"))
        {
            groupBypassGlobalPrefix = true;
        }
        else if (!string.IsNullOrEmpty(groupRoutePrefix))
        {
            groupRoutePrefix = groupRoutePrefix!.TrimStart('/');
        }

        // Explicit route
        var explicitRoute = GetStringProperty(methodEndpointAttr, "Route")
                         ?? GetStringProperty(classEndpointAttr, "Route");

        bool routeBypassPrefixes = false;
        if (explicitRoute != null && explicitRoute.StartsWith("/"))
            routeBypassPrefixes = true;

        // Auto-derive group from handler class name when an explicit relative route
        // is set but no [HandlerEndpointGroup] is present.
        if (groupAttr == null && !string.IsNullOrEmpty(explicitRoute) && !routeBypassPrefixes)
        {
            var handlerPrefix = RouteConventions.GetHandlerPrefix(containingType.Name);
            if (!string.IsNullOrEmpty(handlerPrefix))
            {
                groupName ??= handlerPrefix;
                groupRoutePrefix ??= handlerPrefix!.ToKebabCase();
            }
        }

        string route;
        if (!string.IsNullOrEmpty(explicitRoute))
        {
            route = explicitRoute!;
        }
        else
        {
            // Compute route parameters (ID properties of the message type)
            var actionVerb = RouteConventions.GetActionVerb(messageType.Name);
            var routeParamNames = GetRouteParameterNames(messageType, httpMethod, actionVerb != null);
            route = RouteConventions.GenerateRoute(messageType.Name, groupRoutePrefix, groupName, routeParamNames, httpMethod, actionVerb, containingType.Name);
        }

        // Compute full display route
        var globalPrefix = config.RoutePrefix ?? "";
        var fullRoute = RouteConventions.ComputeFullDisplayRoute(
            globalPrefix, groupRoutePrefix ?? "", route, groupBypassGlobalPrefix, routeBypassPrefixes);

        return new RouteResult(httpMethod, fullRoute);
    }

    /// <summary>
    /// Gets the route parameter names from a message type (simplified version of AnalyzeMessageParameters).
    /// Only extracts ID properties that become route segments.
    /// </summary>
    private static string[] GetRouteParameterNames(INamedTypeSymbol messageType, string httpMethod, bool isActionVerb)
    {
        var routeParams = new List<string>();

        foreach (var member in messageType.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;
            if (prop.DeclaredAccessibility != Accessibility.Public || prop.GetMethod == null)
                continue;

            // Check for explicit [FromRoute] attribute
            bool isExplicitRoute = prop.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "Microsoft.AspNetCore.Mvc.FromRouteAttribute");
            if (isExplicitRoute)
            {
                routeParams.Add(prop.Name.ToCamelCase());
                continue;
            }

            bool isIdProperty = prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)
                             || prop.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase);

            if (isIdProperty && (httpMethod is "GET" or "DELETE" or "PUT" || isActionVerb))
                routeParams.Add(prop.Name.ToCamelCase());
        }

        return routeParams.ToArray();
    }

    #endregion

    #region Handler Search for Messages

    private readonly record struct HandlerMatch(string ClassName, string MethodName, string MessageTypeName, string FileName, IMethodSymbol Method);

    /// <summary>
    /// Searches the compilation's source-declared types for a handler method that handles the given message type.
    /// Uses the assembly's global namespace to walk type hierarchies instead of GetSemanticModel per tree.
    /// </summary>
    private static HandlerMatch? FindHandlerForMessage(INamedTypeSymbol messageType, Compilation compilation)
    {
        // Walk all source-declared types via the compilation's global namespace
        return SearchNamespace(compilation.Assembly.GlobalNamespace, messageType, compilation);
    }

    private static HandlerMatch? SearchNamespace(INamespaceSymbol ns, INamedTypeSymbol messageType, Compilation compilation)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol childNs)
            {
                if (childNs.ToDisplayString() == WellKnownTypes.GeneratedNamespace)
                    continue;
                var match = SearchNamespace(childNs, messageType, compilation);
                if (match != null)
                    return match;
            }
            else if (member is INamedTypeSymbol type)
            {
                var match = SearchType(type, messageType, compilation);
                if (match != null)
                    return match;
            }
        }

        return null;
    }

    private static HandlerMatch? SearchType(INamedTypeSymbol type, INamedTypeSymbol messageType, Compilation compilation)
    {
        // Only consider source-declared types
        if (type.Locations.All(l => !l.IsInSource))
            return null;

        bool isHandlerByConvention = IsHandlerClass(type, compilation);

        foreach (var member in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (!IsHandlerMethod(member, compilation, isHandlerByConvention))
                continue;

            if (member.Parameters.Length == 0)
                continue;

            var paramType = member.Parameters[0].Type;
            if (SymbolEqualityComparer.Default.Equals(paramType, messageType))
            {
                var filePath = member.Locations.FirstOrDefault()?.SourceTree?.FilePath;
                var fileName = filePath != null ? System.IO.Path.GetFileName(filePath) : "unknown";
                return new HandlerMatch(type.Name, member.Name, paramType.Name, fileName, member);
            }
        }

        // Check nested types
        foreach (var nestedType in type.GetTypeMembers())
        {
            var match = SearchType(nestedType, messageType, compilation);
            if (match != null)
                return match;
        }

        return null;
    }

    #endregion

    #region Configuration

    private readonly record struct EndpointConfig(string Discovery, string? RoutePrefix);

    private static EndpointConfig ReadEndpointConfig(Compilation compilation)
    {
        string discovery = "All";
        string? routePrefix = "/api";

        var configAttr = compilation.Assembly.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.MediatorConfigurationAttribute);

        if (configAttr != null)
        {
            foreach (var arg in configAttr.NamedArguments)
            {
                switch (arg.Key)
                {
                    case "EndpointDiscovery" when arg.Value.Value is int v:
                        discovery = v switch { 1 => "Explicit", 2 => "All", _ => "None" };
                        break;
                    case "EndpointRoutePrefix" when arg.Value.Value is string s:
                        routePrefix = s;
                        break;
                }
            }
        }

        return new EndpointConfig(discovery, routePrefix);
    }

    #endregion

    #region Attribute Helpers

    private static bool? GetBoolProperty(AttributeData? attr, string name)
    {
        if (attr == null) return null;
        var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
        return arg.Value.Value as bool?;
    }

    private static string? GetStringProperty(AttributeData? attr, string name)
    {
        if (attr == null) return null;
        var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
        return arg.Value.Value as string;
    }

    private static int? GetIntProperty(AttributeData? attr, string name)
    {
        if (attr == null) return null;
        var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
        return arg.Value.Value as int?;
    }

    #endregion
}
