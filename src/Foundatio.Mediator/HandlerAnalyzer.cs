using System.Xml.Linq;
using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class HandlerAnalyzer
{
    public static bool IsMatch(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax { Identifier.ValueText: var name } classDecl)
            return false;

        if (name.EndsWith("Handler") || name.EndsWith("Consumer"))
            return true;

        if (classDecl.BaseList is { Types.Count: > 0 })
        {
            foreach (var bt in classDecl.BaseList.Types)
            {
                string? typeName = bt.Type switch
                {
                    SimpleNameSyntax sns => sns.Identifier.ValueText,
                    QualifiedNameSyntax qns => qns.Right.Identifier.ValueText,
                    AliasQualifiedNameSyntax aq => aq.Name.Identifier.ValueText,
                    _ => (bt.Type as IdentifierNameSyntax)?.Identifier.ValueText
                };

                if (typeName == "IHandler")
                    return true;
            }
        }

        if (classDecl.AttributeLists.Count > 0 && classDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Name is IdentifierNameSyntax { Identifier.ValueText: "Handler" }
                    or QualifiedNameSyntax
                {
                    Right.Identifier.ValueText: "Handler"
                }))
        {
            return true;
        }

        foreach (var member in classDecl.Members)
        {
            if (member is not MethodDeclarationSyntax m || m.AttributeLists.Count <= 0)
                continue;

            if (m.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name is IdentifierNameSyntax { Identifier.ValueText: "Handler" }
                    or QualifiedNameSyntax
                {
                    Right.Identifier.ValueText: "Handler"
                }))
            {
                return true;
            }
        }

        return false;
    }

    public static List<HandlerInfo> GetHandlers(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not { } classSymbol
            || classSymbol.HasIgnoreAttribute(context.SemanticModel.Compilation))
            return [];

        // Exclude generated handler classes in Foundatio.Mediator namespace with names ending in "_Handler"
        if (classSymbol.ContainingNamespace?.ToDisplayString() == "Foundatio.Mediator" &&
            classSymbol.Name.EndsWith("_Handler"))
            return [];

        // Exclude nested classes inside generic types
        if (IsNestedInGenericType(classSymbol))
            return [];

        // Determine if the class should be treated as a handler class
        bool nameMatches = classSymbol.Name.EndsWith("Handler") || classSymbol.Name.EndsWith("Consumer");
        bool implementsMarker = classSymbol.AllInterfaces.Any(i => i.ToDisplayString() == "Foundatio.Mediator.IHandler");
        bool hasClassHandlerAttribute = classSymbol.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute);

        // Explicit discovery: IHandler interface or [Handler] attribute on class
        bool isExplicitlyDeclared = implementsMarker || hasClassHandlerAttribute;

        bool treatAsHandlerClass = nameMatches || implementsMarker || hasClassHandlerAttribute;

        var handlerMethods = GetMethods(classSymbol)
            .Where(m => IsHandlerMethod(m, context.SemanticModel.Compilation, treatAsHandlerClass))
            .ToList();

        if (handlerMethods.Count == 0)
            return [];

        var handlers = new List<HandlerInfo>();

        foreach (var handlerMethod in handlerMethods)
        {
            if (handlerMethod.Parameters.Length == 0)
                continue;

            if (handlerMethod.IsGenericMethod)
                continue; // do not support generic handler methods, only generic classes

            var messageParameter = handlerMethod.Parameters[0];
            var messageType = messageParameter.Type;

            var parameterInfos = new List<ParameterInfo>();

            foreach (var parameter in handlerMethod.Parameters)
            {
                bool isMessage = SymbolEqualityComparer.Default.Equals(parameter, messageParameter);

                parameterInfos.Add(new ParameterInfo
                {
                    Name = parameter.Name,
                    Type = TypeSymbolInfo.From(parameter.Type, context.SemanticModel.Compilation),
                    IsMessageParameter = isMessage
                });
            }

            string? messageGenericDefinition = null;
            int messageGenericArity = 0;
            if (messageType is INamedTypeSymbol namedMsg && namedMsg.IsGenericType)
            {
                messageGenericDefinition = namedMsg.ConstructUnboundGenericType().ToDisplayString();
                messageGenericArity = namedMsg.TypeArguments.Length;
            }

            string[] genericParamNames;
            string[] genericConstraints;
            if (classSymbol.IsGenericType)
            {
                genericParamNames = classSymbol.TypeParameters.Select(tp => tp.Name).ToArray();
                genericConstraints = classSymbol.TypeParameters.Select(BuildConstraintClause).Where(s => s.Length > 0).ToArray();
            }
            else
            {
                genericParamNames = [];
                genericConstraints = [];
            }

            var messageInterfaces = new List<string>();
            var messageBaseClasses = new List<string>();

            if (messageType is INamedTypeSymbol namedMessageType)
            {
                foreach (var iface in namedMessageType.AllInterfaces)
                {
                    messageInterfaces.Add(iface.ToDisplayString());
                }

                var currentBase = namedMessageType.BaseType;
                while (currentBase != null && currentBase.SpecialType != SpecialType.System_Object)
                {
                    messageBaseClasses.Add(currentBase.ToDisplayString());
                    currentBase = currentBase.BaseType;
                }
            }

            // A handler method is explicitly declared if:
            // 1. The class implements IHandler interface (isExplicitlyDeclared includes implementsMarker)
            // 2. The class has [Handler] attribute (isExplicitlyDeclared includes hasClassHandlerAttribute)
            // 3. The method has [Handler] attribute
            bool hasMethodHandlerAttribute = handlerMethod.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute);
            bool methodIsExplicitlyDeclared = isExplicitlyDeclared || hasMethodHandlerAttribute;

            // Extract Order and Lifetime from [Handler] attribute (method attribute takes precedence over class attribute)
            int order = int.MaxValue;
            string? lifetime = null;
            var handlerAttr = hasMethodHandlerAttribute
                ? handlerMethod.GetAttributes().FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute)
                : hasClassHandlerAttribute
                    ? classSymbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute)
                    : null;

            if (handlerAttr != null)
            {
                // Check constructor argument for order
                if (handlerAttr.ConstructorArguments.Length > 0 && handlerAttr.ConstructorArguments[0].Value is int orderValue)
                    order = orderValue;

                // Check named argument (property) for Order
                var orderArg = handlerAttr.NamedArguments.FirstOrDefault(na => na.Key == "Order");
                if (orderArg.Value.Value is int namedOrderValue)
                    order = namedOrderValue;

                // Check named argument (property) for Lifetime
                // The enum value is stored as an int in the attribute data
                var lifetimeArg = handlerAttr.NamedArguments.FirstOrDefault(na => na.Key == "Lifetime");
                if (lifetimeArg.Value.Value is int lifetimeValue && lifetimeValue > 0)
                {
                    // Map enum values: 0=Default, 1=Transient, 2=Scoped, 3=Singleton
                    lifetime = lifetimeValue switch
                    {
                        1 => "Transient",
                        2 => "Scoped",
                        3 => "Singleton",
                        _ => null // 0 (Default) means use project-level default
                    };
                }
            }

            // Check if the handler has constructor parameters (indicating DI dependencies)
            bool hasConstructorParameters = !handlerMethod.IsStatic &&
                classSymbol.InstanceConstructors.Any(c => c.Parameters.Length > 0);

            // Extract XML documentation summary
            var xmlDocSummary = ExtractXmlDocSummary(handlerMethod);

            // Extract endpoint metadata
            var endpointInfo = ExtractEndpointInfo(
                classSymbol,
                handlerMethod,
                messageType as INamedTypeSymbol,
                xmlDocSummary,
                context.SemanticModel.Compilation);

            handlers.Add(new HandlerInfo
            {
                Identifier = classSymbol.Name.ToIdentifier(),
                FullName = classSymbol.ToDisplayString(),
                MethodName = handlerMethod.Name,
                MessageType = TypeSymbolInfo.From(messageType, context.SemanticModel.Compilation),
                MessageInterfaces = new(messageInterfaces.ToArray()),
                MessageBaseClasses = new(messageBaseClasses.ToArray()),
                ReturnType = TypeSymbolInfo.From(handlerMethod.ReturnType, context.SemanticModel.Compilation),
                IsStatic = handlerMethod.IsStatic,
                IsGenericHandlerClass = classSymbol.IsGenericType,
                GenericArity = classSymbol.IsGenericType ? classSymbol.TypeParameters.Length : 0,
                GenericTypeParameters = new(genericParamNames),
                MessageGenericTypeDefinitionFullName = messageGenericDefinition,
                MessageGenericArity = messageGenericArity,
                GenericConstraints = new(genericConstraints),
                Parameters = new(parameterInfos.ToArray()),
                CallSites = [],
                Middleware = [],
                IsExplicitlyDeclared = methodIsExplicitlyDeclared,
                Order = order,
                Lifetime = lifetime,
                HasConstructorParameters = hasConstructorParameters,
                Endpoint = endpointInfo,
                XmlDocSummary = xmlDocSummary,
            });
        }

        return handlers;
    }

    private static bool IsHandlerMethod(IMethodSymbol method, Compilation compilation, bool treatAsHandlerClass)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
            return false;

        bool hasMethodHandlerAttribute = method.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute);
        if (!treatAsHandlerClass && !hasMethodHandlerAttribute)
            return false;

        if (treatAsHandlerClass && !ValidHandlerMethodNames.Contains(method.Name))
            return false;

        if (method.HasIgnoreAttribute(compilation))
            return false;

        if (method.IsMassTransitConsumeMethod())
            return false;

        return true;
    }

    private static IEnumerable<IMethodSymbol> GetMethods(INamedTypeSymbol targetSymbol, bool includeBaseMethods = true)
    {
        var methods = new Dictionary<string, IMethodSymbol>();

        var currentSymbol = targetSymbol;

        while (currentSymbol != null)
        {
            var methodSymbols = currentSymbol
                .GetMembers()
                .Where(m => m.Kind == SymbolKind.Method)
                .OfType<IMethodSymbol>();

            foreach (var methodSymbol in methodSymbols)
            {
                string signature = BuildMethodSignature(methodSymbol);

                if (!methods.ContainsKey(signature))
                    methods.Add(signature, methodSymbol);
            }

            if (!includeBaseMethods)
                break;

            currentSymbol = currentSymbol.BaseType;
        }

        return methods.Values;
    }

    private static string BuildMethodSignature(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
            return method.Name + "()";

        string[] parts = new string[method.Parameters.Length];
        for (int i = 0; i < method.Parameters.Length; i++)
            parts[i] = method.Parameters[i].Type.ToDisplayString();

        return method.Name + "(" + String.Join(",", parts) + ")";
    }

    private static readonly string[] ValidHandlerMethodNames = [
        "Handle", "HandleAsync",
        "Handles", "HandlesAsync",
        "Consume", "ConsumeAsync",
        "Consumes", "ConsumesAsync"
    ];

    private static string BuildConstraintClause(ITypeParameterSymbol tp)
    {
        var ordered = new List<string>();

        if (tp.HasReferenceTypeConstraint)
            ordered.Add("class");
        else if (tp.HasValueTypeConstraint)
            ordered.Add("struct");
        else if (tp.HasUnmanagedTypeConstraint)
            ordered.Add("unmanaged");

        foreach (var c in tp.ConstraintTypes)
        {
            string display = c.ToDisplayString();
            if (!ordered.Contains(display))
                ordered.Add(display);
        }

        if (tp.HasNotNullConstraint)
            ordered.Add("notnull");
        if (tp.HasConstructorConstraint)
            ordered.Add("new()");

        if (ordered.Count == 0)
            return String.Empty;

        return $"where {tp.Name} : {String.Join(", ", ordered)}";
    }

    private static bool IsNestedInGenericType(INamedTypeSymbol typeSymbol)
    {
        var containingType = typeSymbol.ContainingType;
        while (containingType != null)
        {
            if (containingType.IsGenericType)
                return true;
            containingType = containingType.ContainingType;
        }
        return false;
    }

    #region Endpoint Extraction

    /// <summary>
    /// Extracts the XML documentation summary from a method symbol.
    /// </summary>
    private static string? ExtractXmlDocSummary(IMethodSymbol method)
    {
        var xmlDoc = method.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xmlDoc))
            return null;

        try
        {
            var doc = XDocument.Parse(xmlDoc);
            var summary = doc.Descendants("summary").FirstOrDefault();
            if (summary == null)
                return null;

            // Get text content and clean it up
            var text = summary.Value?.Trim();
            if (string.IsNullOrEmpty(text))
                return null;

            // Normalize whitespace
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts endpoint metadata from handler attributes and message type.
    /// </summary>
    private static EndpointInfo? ExtractEndpointInfo(
        INamedTypeSymbol classSymbol,
        IMethodSymbol handlerMethod,
        INamedTypeSymbol? messageType,
        string? xmlDocSummary,
        Compilation compilation)
    {
        if (messageType == null)
            return null;

        // Get [HandlerEndpoint] attribute from method or class
        var methodEndpointAttr = handlerMethod.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerEndpointAttribute);
        var classEndpointAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerEndpointAttribute);

        // Get [HandlerCategory] attribute from class
        var categoryAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerCategoryAttribute);

        // Check if explicitly excluded via attribute
        bool isExcluded = GetBoolProperty(methodEndpointAttr, "Exclude") ??
                          GetBoolProperty(classEndpointAttr, "Exclude") ??
                          false;

        if (isExcluded)
        {
            return new EndpointInfo { GenerateEndpoint = false };
        }

        // Auto-exclude events/notifications from endpoint generation
        if (ShouldExcludeAsEvent(classSymbol, messageType))
        {
            return new EndpointInfo { GenerateEndpoint = false };
        }

        // Extract category info
        string? categoryName = null;
        string? categoryRoutePrefix = null;
        bool? categoryRequireAuth = null;
        string[]? categoryRoles = null;
        string? categoryPolicy = null;

        if (categoryAttr != null)
        {
            // Category name is the constructor argument
            if (categoryAttr.ConstructorArguments.Length > 0)
                categoryName = categoryAttr.ConstructorArguments[0].Value as string;

            categoryRoutePrefix = GetStringProperty(categoryAttr, "RoutePrefix");
            // If no explicit RoutePrefix, use the category name as the prefix (lowercase)
            if (string.IsNullOrEmpty(categoryRoutePrefix) && !string.IsNullOrEmpty(categoryName))
            {
                categoryRoutePrefix = "/" + categoryName!.ToLowerInvariant();
            }
            categoryRequireAuth = GetBoolProperty(categoryAttr, "RequireAuth");
            categoryRoles = GetStringArrayProperty(categoryAttr, "Roles");
            categoryPolicy = GetStringProperty(categoryAttr, "Policy");
        }

        // Extract endpoint info (method takes precedence over class)
        var httpMethod = GetStringProperty(methodEndpointAttr, "HttpMethod") ??
                         GetStringProperty(classEndpointAttr, "HttpMethod") ??
                         InferHttpMethod(messageType.Name);

        var explicitRoute = GetStringProperty(methodEndpointAttr, "Route") ??
                            GetStringProperty(classEndpointAttr, "Route");
        var hasExplicitRoute = !string.IsNullOrEmpty(explicitRoute);
        var route = explicitRoute;

        var name = GetStringProperty(methodEndpointAttr, "Name") ??
                   GetStringProperty(classEndpointAttr, "Name") ??
                   GetEndpointName(messageType);

        var summary = GetStringProperty(methodEndpointAttr, "Summary") ??
                      GetStringProperty(classEndpointAttr, "Summary") ??
                      xmlDocSummary;

        var description = GetStringProperty(methodEndpointAttr, "Description") ??
                          GetStringProperty(classEndpointAttr, "Description");

        var tags = GetStringArrayProperty(methodEndpointAttr, "Tags") ??
                   GetStringArrayProperty(classEndpointAttr, "Tags");

        // Auth configuration (method -> class -> category)
        var requireAuth = GetBoolProperty(methodEndpointAttr, "RequireAuth") ??
                          GetBoolProperty(classEndpointAttr, "RequireAuth") ??
                          categoryRequireAuth ??
                          false;

        var roles = GetStringArrayProperty(methodEndpointAttr, "Roles") ??
                    GetStringArrayProperty(classEndpointAttr, "Roles") ??
                    categoryRoles ??
                    [];

        var policy = GetStringProperty(methodEndpointAttr, "Policy") ??
                     GetStringProperty(classEndpointAttr, "Policy") ??
                     categoryPolicy;

        var policies = GetStringArrayProperty(methodEndpointAttr, "Policies") ??
                       GetStringArrayProperty(classEndpointAttr, "Policies") ??
                       [];

        // Combine single policy with policies array
        var allPolicies = policy != null
            ? policies.Prepend(policy).Distinct().ToArray()
            : policies;

        // Analyze message type for parameters
        var (routeParams, queryParams, supportsAsParameters) = AnalyzeMessageParameters(messageType, httpMethod, compilation);

        // Generate route if not explicitly specified
        if (string.IsNullOrEmpty(route))
        {
            route = GenerateRoute(messageType.Name, categoryRoutePrefix, routeParams, httpMethod);
        }

        // Determine binding strategy
        bool bindFromBody = httpMethod is "POST" or "PUT" or "PATCH";

        return new EndpointInfo
        {
            HttpMethod = httpMethod,
            Route = route!,
            HasExplicitRoute = hasExplicitRoute,
            Name = name,
            Summary = summary,
            Description = description,
            Category = categoryName ?? tags?.FirstOrDefault(),
            CategoryRoutePrefix = categoryRoutePrefix,
            RouteParameters = new(routeParams),
            QueryParameters = new(queryParams),
            BindFromBody = bindFromBody,
            SupportsAsParameters = supportsAsParameters,
            GenerateEndpoint = true,
            RequireAuth = requireAuth,
            Roles = new(roles),
            Policies = new(allPolicies),
        };
    }

    /// <summary>
    /// Infers the HTTP method from the message type name.
    /// </summary>
    private static string InferHttpMethod(string messageTypeName)
    {
        if (messageTypeName.StartsWith("Get", StringComparison.OrdinalIgnoreCase) ||
            messageTypeName.StartsWith("Find", StringComparison.OrdinalIgnoreCase) ||
            messageTypeName.StartsWith("Search", StringComparison.OrdinalIgnoreCase) ||
            messageTypeName.StartsWith("List", StringComparison.OrdinalIgnoreCase) ||
            messageTypeName.StartsWith("Query", StringComparison.OrdinalIgnoreCase))
            return "GET";

        if (messageTypeName.StartsWith("Create", StringComparison.OrdinalIgnoreCase) ||
            messageTypeName.StartsWith("Add", StringComparison.OrdinalIgnoreCase) ||
            messageTypeName.StartsWith("New", StringComparison.OrdinalIgnoreCase))
            return "POST";

        if (messageTypeName.StartsWith("Update", StringComparison.OrdinalIgnoreCase) ||
            messageTypeName.StartsWith("Edit", StringComparison.OrdinalIgnoreCase) ||
            messageTypeName.StartsWith("Modify", StringComparison.OrdinalIgnoreCase) ||
            messageTypeName.StartsWith("Change", StringComparison.OrdinalIgnoreCase) ||
            messageTypeName.StartsWith("Set", StringComparison.OrdinalIgnoreCase))
            return "PUT";

        if (messageTypeName.StartsWith("Delete", StringComparison.OrdinalIgnoreCase) ||
            messageTypeName.StartsWith("Remove", StringComparison.OrdinalIgnoreCase))
            return "DELETE";

        if (messageTypeName.StartsWith("Patch", StringComparison.OrdinalIgnoreCase))
            return "PATCH";

        return "POST"; // Default
    }

    /// <summary>
    /// Analyzes message type properties to determine route and query parameters.
    /// </summary>
    private static (EndpointParameterInfo[] routeParams, EndpointParameterInfo[] queryParams, bool supportsAsParameters)
        AnalyzeMessageParameters(INamedTypeSymbol messageType, string httpMethod, Compilation compilation)
    {
        var routeParams = new List<EndpointParameterInfo>();
        var queryParams = new List<EndpointParameterInfo>();

        // Check if message supports [AsParameters] binding
        // It needs a parameterless constructor or a record with optional parameters
        bool hasParameterlessConstructor = messageType.InstanceConstructors
            .Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

        bool isRecordWithDefaults = messageType.IsRecord &&
            messageType.InstanceConstructors.Any(c =>
                c.DeclaredAccessibility == Accessibility.Public &&
                c.Parameters.All(p => p.HasExplicitDefaultValue));

        bool supportsAsParameters = hasParameterlessConstructor || isRecordWithDefaults;

        // Get all public properties with getters
        var properties = messageType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && p.GetMethod != null)
            .ToList();

        foreach (var prop in properties)
        {
            var paramInfo = new EndpointParameterInfo
            {
                Name = ToCamelCase(prop.Name),
                PropertyName = prop.Name,
                Type = TypeSymbolInfo.From(prop.Type, compilation),
                IsOptional = prop.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                             prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T,
            };

            // Determine if this should be a route parameter
            bool isIdProperty = prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                                prop.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase);

            // For GET/DELETE/PUT, ID properties become route parameters
            if (isIdProperty && httpMethod is "GET" or "DELETE" or "PUT")
            {
                routeParams.Add(paramInfo with { IsRouteParameter = true });
            }
            else if (httpMethod is "GET" or "DELETE")
            {
                // Non-ID properties become query parameters for GET/DELETE
                queryParams.Add(paramInfo with { IsRouteParameter = false });
            }
        }

        return (routeParams.ToArray(), queryParams.ToArray(), supportsAsParameters);
    }

    /// <summary>
    /// Generates a route template from message name and parameters.
    /// This generates the relative route (what goes after the group prefix).
    /// </summary>
    private static string GenerateRoute(
        string messageTypeName,
        string? categoryRoutePrefix,
        EndpointParameterInfo[] routeParams,
        string httpMethod)
    {
        var parts = new List<string>();

        // If we have a category with a route prefix, the route is relative to that prefix
        // Otherwise, we need to include the entity name in the route
        if (string.IsNullOrEmpty(categoryRoutePrefix))
        {
            // No category prefix - include entity name in route
            var entityName = ToKebabCase(RemoveVerbPrefix(messageTypeName));
            if (!string.IsNullOrEmpty(entityName))
            {
                parts.Add(entityName);
            }
        }

        // Add route parameters
        foreach (var param in routeParams)
        {
            parts.Add($"{{{param.Name}}}");
        }

        // Build the route
        if (parts.Count == 0)
            return "/";

        return "/" + string.Join("/", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>
    /// Removes common verb prefixes from message type names.
    /// </summary>
    private static string RemoveVerbPrefix(string name)
    {
        string[] prefixes = ["Get", "Find", "Search", "List", "Query", "Create", "Add", "New", "Update", "Edit", "Modify", "Change", "Set", "Delete", "Remove", "Patch"];

        foreach (var prefix in prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
            {
                return name.Substring(prefix.Length);
            }
        }

        return name;
    }

    /// <summary>
    /// Converts PascalCase to kebab-case.
    /// </summary>
    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c))
            {
                if (result.Length > 0)
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

    #region Event Detection

    /// <summary>
    /// Common suffixes that indicate a message is an event/notification rather than a command/query.
    /// </summary>
    private static readonly string[] EventSuffixes =
    [
        "Created", "Updated", "Deleted", "Changed", "Removed", "Added",
        "Event", "Notification", "Published", "Occurred", "Happened",
        "Started", "Completed", "Failed", "Cancelled", "Expired"
    ];

    /// <summary>
    /// Determines if a handler/message should be excluded from endpoint generation because it's an event.
    /// </summary>
    private static bool ShouldExcludeAsEvent(INamedTypeSymbol classSymbol, INamedTypeSymbol messageType)
    {
        // 1. Check if message implements INotification (MediatR-style)
        if (messageType.AllInterfaces.Any(i =>
            i.Name == "INotification" ||
            i.ToDisplayString() == "Foundatio.Mediator.INotification" ||
            i.ToDisplayString() == "MediatR.INotification"))
        {
            return true;
        }

        // 2. Check if message implements common event marker interfaces
        if (messageType.AllInterfaces.Any(i =>
            i.Name == "IEvent" ||
            i.Name == "IDomainEvent" ||
            i.Name == "IIntegrationEvent"))
        {
            return true;
        }

        // 3. Check if handler class name ends with "EventHandler" or "NotificationHandler"
        if (classSymbol.Name.EndsWith("EventHandler") ||
            classSymbol.Name.EndsWith("NotificationHandler"))
        {
            return true;
        }

        // 4. Check if message type name has common event suffixes
        var messageName = messageType.Name;
        foreach (var suffix in EventSuffixes)
        {
            if (messageName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Attribute Helper Methods

    private static string? GetStringProperty(AttributeData? attr, string propertyName)
    {
        if (attr == null)
            return null;

        var arg = attr.NamedArguments.FirstOrDefault(na => na.Key == propertyName);
        return arg.Value.Value as string;
    }

    private static bool? GetBoolProperty(AttributeData? attr, string propertyName)
    {
        if (attr == null)
            return null;

        var arg = attr.NamedArguments.FirstOrDefault(na => na.Key == propertyName);
        if (arg.Value.Value is bool value)
            return value;

        return null;
    }

    private static string[]? GetStringArrayProperty(AttributeData? attr, string propertyName)
    {
        if (attr == null)
            return null;

        var arg = attr.NamedArguments.FirstOrDefault(na => na.Key == propertyName);
        if (arg.Value.IsNull)
            return null;

        if (arg.Value.Kind == TypedConstantKind.Array)
        {
            return arg.Value.Values
                .Where(v => v.Value is string)
                .Select(v => (string)v.Value!)
                .ToArray();
        }

        return null;
    }

    /// <summary>
    /// Gets a unique endpoint name for a message type, including generic type arguments.
    /// For EntityAction&lt;Order&gt; returns "EntityAction_Order".
    /// </summary>
    private static string GetEndpointName(INamedTypeSymbol messageType)
    {
        if (messageType.IsGenericType && !messageType.IsUnboundGenericType && messageType.TypeArguments.Length > 0)
        {
            var typeArgs = string.Join("_", messageType.TypeArguments.Select(GetTypeArgumentName));
            return $"{messageType.Name}_{typeArgs}";
        }

        return messageType.Name;
    }

    /// <summary>
    /// Gets a name for a type argument, recursively handling nested generic types.
    /// </summary>
    private static string GetTypeArgumentName(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType && !namedType.IsUnboundGenericType)
        {
            var inner = string.Join("_", namedType.TypeArguments.Select(GetTypeArgumentName));
            return $"{namedType.Name}_{inner}";
        }

        return typeSymbol.Name;
    }

    #endregion

    #endregion
}
