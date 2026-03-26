using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;
using System.Globalization;

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
        if (SymbolUtilities.IsNestedInGenericType(classSymbol))
            return [];

        // Determine if the class should be treated as a handler class
        bool nameMatches = classSymbol.Name.EndsWith("Handler") || classSymbol.Name.EndsWith("Consumer");
        bool implementsMarker = classSymbol.AllInterfaces.Any(i => i.ToDisplayString() == WellKnownTypes.IHandler);
        bool hasClassHandlerAttribute = classSymbol.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute);

        // Explicit discovery: IHandler interface or [Handler] attribute on class
        bool isExplicitlyDeclared = implementsMarker || hasClassHandlerAttribute;

        bool treatAsHandlerClass = nameMatches || implementsMarker || hasClassHandlerAttribute;

        var handlerMethods = SymbolUtilities.GetMethods(classSymbol)
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
                genericConstraints = classSymbol.TypeParameters.Select(SymbolUtilities.BuildConstraintClause).Where(s => s.Length > 0).ToArray();
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

            // Extract OrderBefore and OrderAfter from [Handler] attribute
            var orderBefore = ExtractTypeArrayArgument(handlerAttr, "OrderBefore");
            var orderAfter = ExtractTypeArrayArgument(handlerAttr, "OrderAfter");

            // Check if the handler has constructor parameters (indicating DI dependencies)
            bool hasConstructorParameters = !handlerMethod.IsStatic &&
                classSymbol.InstanceConstructors.Any(c => c.Parameters.Length > 0);

            // Extract XML documentation summary (method first, then class if single handler)
            var xmlDocSummary = ExtractXmlDocSummary(handlerMethod)
                ?? (handlerMethods.Count == 1 ? ExtractXmlDocSummary(classSymbol) : null);

            // Extract authorization metadata from [HandlerAuthorize], [HandlerAllowAnonymous], [AllowAnonymous]
            var authorizationInfo = ExtractAuthorizationInfo(classSymbol, handlerMethod, context.SemanticModel.Compilation);

            // Extract endpoint metadata
            var endpointInfo = ExtractEndpointInfo(
                classSymbol,
                handlerMethod,
                messageType as INamedTypeSymbol,
                xmlDocSummary,
                context.SemanticModel.Compilation,
                handlerMethod.ReturnType,
                authorizationInfo);

            // Extract handler-specific middleware references from [UseMiddleware] and custom attributes
            var handlerMiddlewareRefs = ExtractHandlerMiddlewareReferences(
                classSymbol,
                handlerMethod,
                context.SemanticModel.Compilation);

            var attributeMetadata = ExtractAttributeMetadata(classSymbol, handlerMethod, context.SemanticModel.Compilation);

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
                AttributeMetadata = new(attributeMetadata.ToArray()),
                HandlerMiddlewareReferences = new(handlerMiddlewareRefs.ToArray()),
                IsExplicitlyDeclared = methodIsExplicitlyDeclared,
                Order = order,
                OrderBefore = new(orderBefore),
                OrderAfter = new(orderAfter),
                Lifetime = lifetime,
                HasConstructorParameters = hasConstructorParameters,
                Authorization = authorizationInfo,
                Endpoint = endpointInfo,
                XmlDocSummary = xmlDocSummary,
            });
        }

        return handlers;
    }

    private static List<HandlerAttributeMetadataInfo> ExtractAttributeMetadata(INamedTypeSymbol classSymbol, IMethodSymbol handlerMethod, Compilation compilation)
    {
        var metadata = new List<HandlerAttributeMetadataInfo>();

        foreach (var attr in classSymbol.GetAttributes())
        {
            var entry = CreateAttributeMetadata(attr, isMethodLevel: false, compilation);
            if (entry.HasValue)
                metadata.Add(entry.Value);
        }

        foreach (var attr in handlerMethod.GetAttributes())
        {
            var entry = CreateAttributeMetadata(attr, isMethodLevel: true, compilation);
            if (entry.HasValue)
                metadata.Add(entry.Value);
        }

        return metadata;
    }

    private static HandlerAttributeMetadataInfo? CreateAttributeMetadata(AttributeData attributeData, bool isMethodLevel, Compilation compilation)
    {
        var attributeClass = attributeData.AttributeClass;
        if (attributeClass == null)
            return null;

        // Capture extension attribute metadata only when the attribute type comes from
        // a referenced assembly. This avoids churn from app-local attributes while
        // enabling external middleware packages to project their own metadata.
        if (string.Equals(attributeClass.ContainingAssembly?.Name, compilation.AssemblyName, StringComparison.Ordinal))
            return null;

        // Exclude framework attributes defined in the Abstractions assembly —
        // the generator already processes these internally.
        if (string.Equals(attributeClass.ContainingAssembly?.Name, "Foundatio.Mediator.Abstractions", StringComparison.Ordinal))
            return null;

        var attributeTypeNameRaw = attributeClass
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);
        if (string.IsNullOrWhiteSpace(attributeTypeNameRaw))
            return null;

        var attributeTypeName = attributeTypeNameRaw!;

        var constructorArguments = attributeData.ConstructorArguments
            .Select(value => new AttributeValueInfo { Value = FormatTypedConstant(value) })
            .ToArray();

        var namedArguments = attributeData.NamedArguments
            .Select(na => new NamedAttributeArgumentInfo
            {
                Name = na.Key,
                Value = FormatTypedConstant(na.Value)
            })
            .ToArray();

        return new HandlerAttributeMetadataInfo
        {
            AttributeTypeName = attributeTypeName,
            IsMethodLevel = isMethodLevel,
            ConstructorArguments = new(constructorArguments),
            NamedArguments = new(namedArguments)
        };
    }

    private static string? FormatTypedConstant(TypedConstant constant)
    {
        if (constant.IsNull)
            return null;

        if (constant.Kind == TypedConstantKind.Array)
        {
            var values = constant.Values
                .Select(FormatTypedConstant)
                .Select(v => v ?? "null")
                .ToArray();
            return "[" + string.Join(",", values) + "]";
        }

        if (constant.Value is ITypeSymbol typeSymbol)
            return typeSymbol.ToDisplayString();

        if (constant.Value is INamedTypeSymbol namedType)
            return namedType.ToDisplayString();

        if (constant.Value is bool boolean)
            return boolean ? "true" : "false";

        if (constant.Value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);

        return constant.Value?.ToString();
    }

    private static bool IsHandlerMethod(IMethodSymbol method, Compilation compilation, bool treatAsHandlerClass)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
            return false;

        bool hasMethodHandlerAttribute = method.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute);
        if (!treatAsHandlerClass && !hasMethodHandlerAttribute)
            return false;

        if (treatAsHandlerClass && !SymbolUtilities.ValidHandlerMethodNames.Contains(method.Name))
            return false;

        if (method.HasIgnoreAttribute(compilation))
            return false;

        if (method.IsMassTransitConsumeMethod())
            return false;

        return true;
    }



    #region Endpoint Extraction

    /// <summary>
    /// Extracts a Type[] named argument from an attribute, returning the fully qualified type names.
    /// </summary>
    private static string[] ExtractTypeArrayArgument(AttributeData? attr, string argumentName)
    {
        if (attr == null)
            return [];

        var arg = attr.NamedArguments.FirstOrDefault(na => na.Key == argumentName);
        if (arg.Value.Kind != TypedConstantKind.Array || arg.Value.Values.IsDefaultOrEmpty)
            return [];

        var types = new List<string>();
        foreach (var typedConstant in arg.Value.Values)
        {
            if (typedConstant.Value is INamedTypeSymbol typeSymbol)
                types.Add(typeSymbol.ToDisplayString());
        }

        return types.ToArray();
    }

    /// <summary>
    /// Extracts the XML documentation summary from a symbol (method, class, etc.) using syntax trivia.
    /// Requires GenerateDocumentationFile to be enabled for the trivia to be parsed as documentation.
    /// </summary>
    private static string? ExtractXmlDocSummary(ISymbol symbol)
    {
        var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
            return null;

        var syntax = syntaxRef.GetSyntax();

        // Check leading trivia for documentation comments
        foreach (var trivia in syntax.GetLeadingTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) &&
                !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                continue;

            if (trivia.GetStructure() is not DocumentationCommentTriviaSyntax docComment)
                continue;

            // Find the <summary> element
            var summaryElement = docComment.Content
                .OfType<XmlElementSyntax>()
                .FirstOrDefault(e => e.StartTag.Name.ToString() == "summary");

            if (summaryElement == null)
                continue;

            // Extract text content from the summary element
            var textParts = new List<string>();
            foreach (var content in summaryElement.Content)
            {
                if (content is XmlTextSyntax textSyntax)
                {
                    foreach (var token in textSyntax.TextTokens)
                    {
                        textParts.Add(token.ValueText);
                    }
                }
            }

            var text = string.Join("", textParts).Trim();
            if (!string.IsNullOrEmpty(text))
            {
                // Normalize whitespace
                return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts endpoint metadata from handler attributes and message type.
    /// </summary>
    private static EndpointInfo? ExtractEndpointInfo(
        INamedTypeSymbol classSymbol,
        IMethodSymbol handlerMethod,
        INamedTypeSymbol? messageType,
        string? xmlDocSummary,
        Compilation compilation,
        ITypeSymbol returnType,
        AuthorizationInfo authorizationInfo)
    {
        if (messageType == null)
            return null;

        // Get [HandlerEndpoint] attribute from method or class
        var methodEndpointAttr = handlerMethod.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerEndpointAttribute);
        var classEndpointAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerEndpointAttribute);

        // Get [HandlerEndpointGroup] attribute from class
        var categoryAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerEndpointGroupAttribute);

        // Check if explicitly excluded via attribute
        bool isExcluded = GetBoolProperty(methodEndpointAttr, "Exclude") ??
                          GetBoolProperty(classEndpointAttr, "Exclude") ??
                          false;

        if (isExcluded)
        {
            return new EndpointInfo { GenerateEndpoint = false, ExcludeReason = "excluded by attribute" };
        }

        // Auto-exclude events/notifications from endpoint generation
        var eventExcludeReason = GetEventExcludeReason(classSymbol, messageType);
        if (eventExcludeReason != null)
        {
            return new EndpointInfo { GenerateEndpoint = false, ExcludeReason = eventExcludeReason };
        }

        // Extract category info
        string? categoryName = null;
        string? categoryRoutePrefix = null;
        string[]? categoryTags = null;

        if (categoryAttr != null)
        {
            // Category name is the constructor argument
            if (categoryAttr.ConstructorArguments.Length > 0)
                categoryName = categoryAttr.ConstructorArguments[0].Value as string;

            categoryRoutePrefix = GetStringProperty(categoryAttr, "RoutePrefix");
            categoryTags = GetStringArrayProperty(categoryAttr, "Tags");
            // If no explicit RoutePrefix, use the category name as the prefix (lowercase, no leading / = relative)
            if (string.IsNullOrEmpty(categoryRoutePrefix) && !string.IsNullOrEmpty(categoryName))
            {
                categoryRoutePrefix = categoryName!.ToLowerInvariant();
            }
        }

        // A leading / on the category RoutePrefix means absolute (bypass global prefix),
        // matching ASP.NET Core MVC's attribute routing convention.
        // No leading / means relative (nested under global prefix).
        bool categoryBypassGlobalPrefix = false;
        if (!string.IsNullOrEmpty(categoryRoutePrefix) && categoryRoutePrefix!.StartsWith("/", StringComparison.Ordinal))
        {
            categoryBypassGlobalPrefix = true;
        }
        else if (!string.IsNullOrEmpty(categoryRoutePrefix))
        {
            // Ensure relative prefixes don't start with / in the generated MapGroup call
            // (they're relative to the parent group)
            categoryRoutePrefix = categoryRoutePrefix!.TrimStart('/');
        }

        // Extract endpoint info (method takes precedence over class)
        // Extract streaming configuration
        var streamingEnumValue = GetIntProperty(methodEndpointAttr, "Streaming") ??
                                 GetIntProperty(classEndpointAttr, "Streaming") ?? 0;
        var sseEventType = GetStringProperty(methodEndpointAttr, "SseEventType") ??
                           GetStringProperty(classEndpointAttr, "SseEventType");
        // 0 = Default, 1 = ServerSentEvents
        string streamingFormat = streamingEnumValue == 1 ? "ServerSentEvents" : "Default";

        // Detect IAsyncEnumerable return type for streaming
        bool isAsyncEnumerable = returnType.IsAsyncEnumerable(compilation, out var asyncElementType);
        string? streamingItemType = asyncElementType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bool isStreaming = isAsyncEnumerable;

        // If SSE is explicitly requested via attribute but return type isn't IAsyncEnumerable, clear it
        if (!isAsyncEnumerable)
        {
            streamingFormat = "Default";
            isStreaming = false;
        }

        var httpMethodEnum = GetIntProperty(methodEndpointAttr, "HttpMethod") ??
                             GetIntProperty(classEndpointAttr, "HttpMethod") ?? 0;
        var httpMethod = httpMethodEnum switch
        {
            1 => "GET",
            2 => "POST",
            3 => "PUT",
            4 => "DELETE",
            5 => "PATCH",
            _ => isStreaming ? "GET" : InferHttpMethod(messageType.Name)
        };

        var explicitRoute = GetStringProperty(methodEndpointAttr, "Route") ??
                            GetStringProperty(classEndpointAttr, "Route");
        var hasExplicitRoute = !string.IsNullOrEmpty(explicitRoute);

        // A leading / on an explicit route means absolute (bypass all prefixes),
        // matching ASP.NET Core MVC's attribute routing convention.
        bool routeBypassPrefixes = false;
        if (explicitRoute != null && explicitRoute.StartsWith("/", StringComparison.Ordinal))
        {
            routeBypassPrefixes = true;
        }

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

        // Auth derived from unified AuthorizationInfo (populated from [HandlerAuthorize], [HandlerAllowAnonymous], assembly defaults)
        var allowAnonymous = authorizationInfo.AllowAnonymous;
        var requireAuth = authorizationInfo.Required;
        var roles = authorizationInfo.Roles.ToArray();
        var allPolicies = authorizationInfo.Policies.ToArray();

        // Extract endpoint filters (method -> class -> merge; category is separate)
        var methodFilters = GetTypeArrayProperty(methodEndpointAttr, "EndpointFilters");
        var classFilters = GetTypeArrayProperty(classEndpointAttr, "EndpointFilters");
        var endpointFilters = (methodFilters ?? []).Concat(classFilters ?? []).Distinct().ToArray();
        var categoryFilters = GetTypeArrayProperty(categoryAttr, "EndpointFilters") ?? [];

        // Extract ProducesStatusCodes from [HandlerEndpoint] attribute (method -> class)
        var explicitStatusCodes = GetIntArrayProperty(methodEndpointAttr, "ProducesStatusCodes") ??
                                  GetIntArrayProperty(classEndpointAttr, "ProducesStatusCodes");

        // Auto-detect Result factory method calls in the handler body (Created, NotFound, Invalid, etc.)
        var detectedStatusCodes = DetectResultStatusCodes(handlerMethod, returnType, compilation, out var usesResultCreated);

        // If no explicit status codes, use auto-detected ones
        var producesStatusCodes = explicitStatusCodes ?? detectedStatusCodes;

        // Read explicit success status code (method -> class, 0 means auto-detect)
        var explicitSuccessStatusCode = GetIntProperty(methodEndpointAttr, "SuccessStatusCode")
                                     ?? GetIntProperty(classEndpointAttr, "SuccessStatusCode")
                                     ?? 0;

        // Extract ProducesType from return type for auto Produces<T>() generation
        string? producesType = ExtractProducesType(returnType, compilation);

        // Detect action verb for route generation (e.g., CompleteTodo → "complete")
        var actionVerb = GetActionVerb(messageType.Name);

        // Analyze message type for parameters
        var (routeParams, queryParams, bindingParams, supportsAsParameters, hasParameterlessConstructor) = AnalyzeMessageParameters(messageType, httpMethod, compilation, isActionVerb: actionVerb != null);

        // Generate route if not explicitly specified
        if (string.IsNullOrEmpty(route))
        {
            route = GenerateRoute(messageType.Name, categoryRoutePrefix, categoryName, routeParams, httpMethod, actionVerb);
        }

        // Determine binding strategy
        bool bindFromBody = httpMethod is "POST" or "PUT" or "PATCH";

        // For auto-generated action verb routes, check if all properties are already
        // covered by route params (IDs). If so, skip body binding.
        if (bindFromBody && actionVerb != null && !hasExplicitRoute)
        {
            var allProperties = messageType.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public && p.GetMethod != null)
                .ToList();

            if (allProperties.Count > 0 && allProperties.All(p =>
                    routeParams.Any(rp => string.Equals(rp.PropertyName, p.Name, StringComparison.Ordinal))))
            {
                bindFromBody = false;
            }
        }

        // If we have an explicit route with placeholders that cover all message properties,
        // skip body binding — all data comes from the route (e.g., POST /{todoId}/complete)
        if (bindFromBody && hasExplicitRoute && !string.IsNullOrEmpty(route))
        {
            var allProperties = messageType.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public && p.GetMethod != null)
                .ToList();

            if (allProperties.Count > 0)
            {
                bool allCoveredByRoute = allProperties.All(p =>
                    route!.Contains($"{{{p.Name.ToCamelCase()}}}", StringComparison.OrdinalIgnoreCase) ||
                    route!.Contains($"{{{p.Name}}}", StringComparison.OrdinalIgnoreCase));

                if (allCoveredByRoute)
                {
                    bindFromBody = false;

                    // Re-extract route params since AnalyzeMessageParameters only extracts
                    // ID properties for GET/DELETE/PUT — now we need all matched properties
                    routeParams = allProperties.Select(p => new EndpointParameterInfo
                    {
                        Name = p.Name.ToCamelCase(),
                        PropertyName = p.Name,
                        Type = TypeSymbolInfo.From(p.Type, compilation),
                        IsRouteParameter = true,
                        IsOptional = p.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                                     p.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T,
                    }).ToArray();
                }
            }
        }

        return new EndpointInfo
        {
            HttpMethod = httpMethod,
            Route = route!,
            HasExplicitRoute = hasExplicitRoute,
            Name = name,
            Summary = summary,
            Description = description,
            Category = categoryName ?? tags?.FirstOrDefault(),
            CategoryTags = new(categoryTags ?? []),
            CategoryRoutePrefix = categoryRoutePrefix,
            CategoryBypassGlobalPrefix = categoryBypassGlobalPrefix,
            RouteBypassPrefixes = routeBypassPrefixes,
            RouteParameters = new(routeParams),
            QueryParameters = new(queryParams),
            BindingParameters = new(bindingParams),
            BindFromBody = bindFromBody,
            SupportsAsParameters = supportsAsParameters,
            HasParameterlessConstructor = hasParameterlessConstructor,
            GenerateEndpoint = true,
            HasExplicitEndpointAttribute = methodEndpointAttr != null || classEndpointAttr != null || categoryAttr != null,
            AllowAnonymous = allowAnonymous,
            RequireAuth = requireAuth,
            Roles = new(roles),
            Policies = new(allPolicies),
            Filters = new(endpointFilters),
            CategoryFilters = new(categoryFilters),
            ProducesType = producesType,
            ProducesStatusCodes = new(producesStatusCodes),
            UsesResultCreated = usesResultCreated,
            ExplicitSuccessStatusCode = explicitSuccessStatusCode,
            IsStreaming = isStreaming,
            StreamingFormat = streamingFormat,
            StreamingItemType = streamingItemType,
            SseEventType = sseEventType,
        };
    }

    /// <summary>
    /// Extracts authorization metadata from [HandlerAuthorize], [HandlerAllowAnonymous], [AllowAnonymous] attributes.
    /// Cascading order: method → class → assembly-level defaults (assembly defaults are applied later by MediatorGenerator).
    /// </summary>
    private static AuthorizationInfo ExtractAuthorizationInfo(
        INamedTypeSymbol classSymbol,
        IMethodSymbol handlerMethod,
        Compilation compilation)
    {
        // Check for [HandlerAuthorize] on method then class
        var methodAuthAttr = handlerMethod.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAuthorizeAttribute);
        var classAuthAttr = classSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAuthorizeAttribute);

        // The presence of [HandlerAuthorize] on either method or class means auth is required
        bool hasHandlerAuthorize = methodAuthAttr != null || classAuthAttr != null;

        // Check for [HandlerAllowAnonymous] or [AllowAnonymous] on method or class
        bool allowAnonymous = handlerMethod.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAllowAnonymousAttribute
                    || a.AttributeClass?.ToDisplayString() == WellKnownTypes.AllowAnonymousAttribute)
            || classSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAllowAnonymousAttribute
                    || a.AttributeClass?.ToDisplayString() == WellKnownTypes.AllowAnonymousAttribute);

        if (!hasHandlerAuthorize)
        {
            // No [HandlerAuthorize] found — return default (Required=false).
            // Assembly-level AuthorizationRequired will be merged later in MediatorGenerator.
            return new AuthorizationInfo
            {
                Required = false,
                AllowAnonymous = allowAnonymous,
                Roles = EquatableArray<string>.Empty,
                Policies = EquatableArray<string>.Empty,
            };
        }

        // Extract roles: method takes precedence over class
        var roles = GetStringArrayProperty(methodAuthAttr, "Roles") ??
                    GetStringArrayProperty(classAuthAttr, "Roles") ??
                    [];

        // Extract policies: method takes precedence over class
        var allPolicies = GetStringArrayProperty(methodAuthAttr, "Policies") ??
                          GetStringArrayProperty(classAuthAttr, "Policies") ??
                          [];

        return new AuthorizationInfo
        {
            Required = true,
            AllowAnonymous = allowAnonymous,
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
    private static (EndpointParameterInfo[] routeParams, EndpointParameterInfo[] queryParams, EndpointParameterInfo[] bindingParams, bool supportsAsParameters, bool hasParameterlessConstructor)
        AnalyzeMessageParameters(INamedTypeSymbol messageType, string httpMethod, Compilation compilation, bool isActionVerb = false)
    {
        var routeParams = new List<EndpointParameterInfo>();
        var queryParams = new List<EndpointParameterInfo>();
        var bindingParams = new List<EndpointParameterInfo>();

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
                Name = prop.Name.ToCamelCase(),
                PropertyName = prop.Name,
                Type = TypeSymbolInfo.From(prop.Type, compilation),
                IsOptional = prop.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                             prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T,
            };

            // Check for explicit binding attributes ([FromHeader], [FromQuery], [FromRoute])
            var bindingAttrSyntax = GetBindingAttributeSyntax(prop);
            if (bindingAttrSyntax != null)
            {
                bindingParams.Add(paramInfo with { BindingAttributeSyntax = bindingAttrSyntax });
                continue;
            }

            // Determine if this should be a route parameter
            bool isIdProperty = prop.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                                prop.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase);

            // For GET/DELETE/PUT, ID properties become route parameters.
            // For action verbs (POST), IDs are also promoted to route params
            // (e.g., CompleteTodo(string TodoId) → POST /todos/{todoId}/complete)
            if (isIdProperty && (httpMethod is "GET" or "DELETE" or "PUT" || isActionVerb))
            {
                routeParams.Add(paramInfo with { IsRouteParameter = true });
            }
            else if (httpMethod is "GET" or "DELETE")
            {
                // Non-ID properties become query parameters for GET/DELETE
                queryParams.Add(paramInfo with { IsRouteParameter = false });
            }
        }

        return (routeParams.ToArray(), queryParams.ToArray(), bindingParams.ToArray(), supportsAsParameters, hasParameterlessConstructor || isRecordWithDefaults);
    }

    /// <summary>
    /// Checks a property for [FromHeader], [FromQuery], or [FromRoute] attributes
    /// and returns the full attribute syntax string for emission, or null if none found.
    /// </summary>
    private static string? GetBindingAttributeSyntax(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var fullName = attrClass.ToDisplayString();
            if (fullName is not ("Microsoft.AspNetCore.Mvc.FromHeaderAttribute"
                              or "Microsoft.AspNetCore.Mvc.FromQueryAttribute"
                              or "Microsoft.AspNetCore.Mvc.FromRouteAttribute"))
                continue;

            // Strip "Attribute" suffix for emission: FromHeaderAttribute → FromHeader
            var attrName = fullName.Substring(0, fullName.Length - "Attribute".Length);

            // Reconstruct attribute arguments
            var args = new List<string>();

            foreach (var ctorArg in attr.ConstructorArguments)
            {
                if (ctorArg.Value is string s)
                    args.Add($"\"{s}\"");
                else if (ctorArg.Value is not null)
                    args.Add(ctorArg.Value.ToString());
            }

            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Value.Value is string s)
                    args.Add($"{namedArg.Key} = \"{s}\"");
                else if (namedArg.Value.Value is not null)
                    args.Add($"{namedArg.Key} = {namedArg.Value.Value}");
            }

            return args.Count > 0
                ? $"[{attrName}({string.Join(", ", args)})]"
                : $"[{attrName}]";
        }

        return null;
    }

    /// <summary>
    /// Generates a route template from message name and parameters.
    /// This generates the relative route (what goes after the group prefix).
    /// </summary>
    private static string GenerateRoute(
        string messageTypeName,
        string? categoryRoutePrefix,
        string? categoryName,
        EndpointParameterInfo[] routeParams,
        string httpMethod,
        string? actionVerb = null)
    {
        var parts = new List<string>();
        var (entityName, lookupSuffix) = NormalizeEntityName(RemoveVerbPrefix(messageTypeName));

        // If we have a category with a route prefix, the route is relative to that prefix
        // Otherwise, we need to include the entity name in the route
        if (string.IsNullOrEmpty(categoryRoutePrefix))
        {
            // No category prefix - include pluralized entity name in route
            var kebabEntity = entityName.SimplePluralize().ToKebabCase();
            if (!string.IsNullOrEmpty(kebabEntity))
            {
                parts.Add(kebabEntity);
            }
        }

        // Add lookup suffix for By<Property>, For<Entity>, From<Entity>, Count patterns
        // e.g., GetTodoByName in "Todos" → /todos/by-name
        //        GetOrdersForCustomer → /orders/for-customer/{customerId}
        // Placed before route params so the suffix reads naturally: /for-customer/{id}
        if (lookupSuffix != null)
        {
            parts.Add(lookupSuffix);
        }

        // Add route parameters
        foreach (var param in routeParams)
        {
            parts.Add($"{{{param.Name}}}");
        }

        // Add action verb suffix for action-style endpoints
        // e.g., CompleteTodo in "Todos" → /{todoId}/complete
        //        CompleteSomething in "Todos" → /{somethingId}/complete-something
        if (actionVerb != null)
        {
            // When entity matches category (or no category), use just the verb: /complete
            // When entity doesn't match, preserve entity in action segment: /complete-something
            bool entityMatchesCategory = !string.IsNullOrEmpty(categoryName) &&
                entityName.Length >= 2 &&
                (categoryName!.StartsWith(entityName, StringComparison.OrdinalIgnoreCase) ||
                 entityName.StartsWith(categoryName, StringComparison.OrdinalIgnoreCase));

            if (entityMatchesCategory || string.IsNullOrEmpty(categoryRoutePrefix))
            {
                parts.Add(actionVerb);
            }
            else
            {
                // Entity doesn't match category — include it to avoid ambiguity
                parts.Add(actionVerb + "-" + entityName.SimplePluralize().ToKebabCase());
            }
        }

        // Build the route
        if (parts.Count == 0)
            return "/";

        return "/" + string.Join("/", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>
    /// All verb prefixes used for route and HTTP method inference.
    /// CRUD verbs map to specific HTTP methods; action verbs default to POST
    /// and generate an action route suffix (e.g., /complete, /archive).
    /// </summary>
    private static readonly string[] CrudPrefixes =
    [
        "Get", "Find", "Search", "List", "Query",
        "Create", "Add", "New",
        "Update", "Edit", "Modify", "Change", "Set",
        "Delete", "Remove",
        "Patch"
    ];

    private static readonly string[] ActionPrefixes =
    [
        "Complete", "Approve", "Cancel", "Submit", "Process",
        "Execute", "Activate", "Deactivate", "Archive", "Restore",
        "Publish", "Unpublish", "Enable", "Disable", "Reset",
        "Confirm", "Reject", "Assign", "Unassign", "Close", "Reopen",
        "Export", "Import", "Download", "Upload"
    ];

    /// <summary>
    /// Returns the action verb (kebab-cased) if the message name starts with an action prefix,
    /// or null for CRUD verbs. Action verbs produce a route suffix (e.g., "complete", "archive")
    /// and promote ID properties to route parameters even for POST.
    /// </summary>
    private static string? GetActionVerb(string messageTypeName)
    {
        foreach (var prefix in ActionPrefixes)
        {
            if (messageTypeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && messageTypeName.Length > prefix.Length)
            {
                return prefix.ToKebabCase();
            }
        }

        return null;
    }

    /// <summary>
    /// Removes common verb prefixes from message type names.
    /// </summary>
    private static string RemoveVerbPrefix(string name)
    {
        foreach (var prefix in CrudPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
            {
                return name.Substring(prefix.Length);
            }
        }

        foreach (var prefix in ActionPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
            {
                return name.Substring(prefix.Length);
            }
        }

        return name;
    }

    /// <summary>
    /// Common suffixes stripped from entity names to normalize routes.
    /// For example, "TodoById" → "Todo", "OrderDetails" → "Order".
    /// Ordered longest-first so "Details" is checked before "Detail".
    /// </summary>
    private static readonly string[] EntitySuffixes =
    [
        "Paginated", "Details", "Detail", "Summary", "ById", "Paged", "Stream", "List"
    ];

    /// <summary>
    /// Normalizes an entity name by removing common qualifiers that break route grouping.
    /// Returns the normalized entity name and an optional route suffix for lookup patterns.
    /// Examples:
    ///   "AllTodos" → ("Todos", null)
    ///   "TodoById" → ("Todo", null)                   — ById stripped, Id becomes route param
    ///   "TodoByName" → ("Todo", "by-name")            — entity extracted, "by-name" becomes route segment
    ///   "TodoDetails" → ("Todo", null)
    ///   "TodoItemsWithPagination" → ("TodoItems", null) — With&lt;Feature&gt; stripped
    ///   "OrderCount" → ("Order", "count")              — count becomes route segment
    ///   "OrdersForCustomer" → ("Orders", "for-customer") — For&lt;Entity&gt; becomes route segment
    ///   "OrdersFromUser" → ("Orders", "from-user")     — From&lt;Entity&gt; becomes route segment
    /// </summary>
    private static (string entityName, string? routeSuffix) NormalizeEntityName(string entityName)
    {
        if (string.IsNullOrEmpty(entityName))
            return (entityName, null);

        // Strip leading "All" prefix (e.g., AllTodos → Todos)
        if (entityName.StartsWith("All", StringComparison.Ordinal) && entityName.Length > 3 && char.IsUpper(entityName[3]))
        {
            entityName = entityName.Substring(3);
        }

        // Strip known suffixes (e.g., TodoById → Todo, OrderDetails → Order, ProductsPaged → Products)
        foreach (var suffix in EntitySuffixes)
        {
            if (entityName.EndsWith(suffix, StringComparison.Ordinal) && entityName.Length > suffix.Length)
            {
                entityName = entityName.Substring(0, entityName.Length - suffix.Length);
                return (entityName, null);
            }
        }

        // Strip With<Feature> suffix entirely (e.g., TodoItemsWithPagination → TodoItems)
        // These are query modifiers (pagination, includes, filters) — not part of the resource identity.
        var withIndex = entityName.IndexOf("With", StringComparison.Ordinal);
        if (withIndex > 0 && withIndex + 4 < entityName.Length && char.IsUpper(entityName[withIndex + 4]))
        {
            entityName = entityName.Substring(0, withIndex);
            return (entityName, null);
        }

        // Detect Count suffix (e.g., OrderCount → entity "Order", suffix "count")
        // This is a well-established REST convention: GET /api/orders/count
        if (entityName.EndsWith("Count", StringComparison.Ordinal) && entityName.Length > 5)
        {
            entityName = entityName.Substring(0, entityName.Length - 5);
            return (entityName, "count");
        }

        // Detect For<Entity>/From<Entity>/By<Property> patterns — extract entity + route suffix.
        // e.g., OrdersForCustomer → ("Orders", "for-customer")
        //        OrdersFromUser → ("Orders", "from-user")
        //        TodoByName → ("Todo", "by-name")
        // "ById" is already handled above via EntitySuffixes.
        foreach (var keyword in new[] { "For", "From", "By" })
        {
            var idx = entityName.IndexOf(keyword, StringComparison.Ordinal);
            if (idx > 0 && idx + keyword.Length < entityName.Length && char.IsUpper(entityName[idx + keyword.Length]))
            {
                var suffix = entityName.Substring(idx);    // e.g., "ForCustomer"
                entityName = entityName.Substring(0, idx); // e.g., "Orders"
                return (entityName, suffix.ToKebabCase());  // e.g., "for-customer"
            }
        }

        return (entityName, null);
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
        => GetEventExcludeReason(classSymbol, messageType) != null;

    /// <summary>
    /// Returns a human-readable reason string if the handler/message should be excluded from endpoint generation
    /// because it's an event, or null if it should not be excluded.
    /// </summary>
    private static string? GetEventExcludeReason(INamedTypeSymbol classSymbol, INamedTypeSymbol messageType)
    {
        // 1. Check if message implements INotification (MediatR-style)
        var notificationInterface = messageType.AllInterfaces.FirstOrDefault(i =>
            i.Name == "INotification" ||
            i.ToDisplayString() == "Foundatio.Mediator.INotification" ||
            i.ToDisplayString() == "MediatR.INotification");
        if (notificationInterface != null)
        {
            return $"implements {notificationInterface.Name}";
        }

        // 2. Check if message implements common event marker interfaces
        var eventInterface = messageType.AllInterfaces.FirstOrDefault(i =>
            i.Name == "IEvent" ||
            i.Name == "IDomainEvent" ||
            i.Name == "IIntegrationEvent");
        if (eventInterface != null)
        {
            return $"implements {eventInterface.Name}";
        }

        // 3. Check if handler class name ends with "EventHandler" or "NotificationHandler"
        if (classSymbol.Name.EndsWith("EventHandler"))
        {
            return "handler name ends with 'EventHandler'";
        }
        if (classSymbol.Name.EndsWith("NotificationHandler"))
        {
            return "handler name ends with 'NotificationHandler'";
        }

        // 4. Check if message type name has common event suffixes
        var messageName = messageType.Name;
        foreach (var suffix in EventSuffixes)
        {
            if (messageName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return $"message name ends with '{suffix}'";
            }
        }

        return null;
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

    private static int? GetIntProperty(AttributeData? attr, string propertyName)
    {
        if (attr == null)
            return null;

        var arg = attr.NamedArguments.FirstOrDefault(na => na.Key == propertyName);
        if (arg.Value.Value is int value)
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
    /// Gets an int[] property value from an attribute.
    /// </summary>
    private static int[]? GetIntArrayProperty(AttributeData? attr, string propertyName)
    {
        if (attr == null)
            return null;

        var arg = attr.NamedArguments.FirstOrDefault(na => na.Key == propertyName);
        if (arg.Value.IsNull)
            return null;

        if (arg.Value.Kind == TypedConstantKind.Array)
        {
            return arg.Value.Values
                .Where(v => v.Value is int)
                .Select(v => (int)v.Value!)
                .ToArray();
        }

        return null;
    }

    /// <summary>
    /// Gets a Type[] property value from an attribute as fully qualified type name strings.
    /// </summary>
    private static string[]? GetTypeArrayProperty(AttributeData? attr, string propertyName)
    {
        if (attr == null)
            return null;

        var arg = attr.NamedArguments.FirstOrDefault(na => na.Key == propertyName);
        if (arg.Value.IsNull)
            return null;

        if (arg.Value.Kind == TypedConstantKind.Array)
        {
            return arg.Value.Values
                .Where(v => v.Value is INamedTypeSymbol)
                .Select(v => ((INamedTypeSymbol)v.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .ToArray();
        }

        return null;
    }

    /// <summary>
    /// Extracts the Produces type from a handler return type.
    /// For Result&lt;T&gt; returns the fully qualified name of T.
    /// For non-Result non-void returns the full return type name.
    /// Returns null for void handlers or non-generic Result.
    /// </summary>
    private static string? ExtractProducesType(ITypeSymbol returnType, Compilation compilation)
    {
        // Unwrap Task/ValueTask
        var unwrapped = returnType.UnwrapTask(compilation);

        // Unwrap nullable
        unwrapped = unwrapped.UnwrapNullable(compilation);

        // Check for void
        if (unwrapped.IsVoid(compilation) || unwrapped.IsTask(compilation))
            return null;

        // Check for IAsyncEnumerable<T> — extract element type T
        if (unwrapped.IsAsyncEnumerable(compilation, out var asyncElementType) && asyncElementType != null)
        {
            return asyncElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        // Check for tuple (cascading messages) - use the first item as the result type
        if (unwrapped is INamedTypeSymbol { IsTupleType: true } tupleType && tupleType.TupleElements.Length > 0)
        {
            unwrapped = tupleType.TupleElements[0].Type;
        }

        // Check for Result<T>
        if (unwrapped.IsResult(compilation) && unwrapped is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 0 } resultType)
        {
            return resultType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        // Non-generic Result (no inner type)
        if (unwrapped.IsResult(compilation))
            return null;

        // Direct return type (not Result, not void)
        return unwrapped.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary>
    /// Maps Result factory method names to HTTP status codes.
    /// </summary>
    private static readonly Dictionary<string, int> ResultMethodToStatusCode = new(StringComparer.Ordinal)
    {
        ["BadRequest"] = 400,
        ["Unauthorized"] = 401,
        ["Forbidden"] = 403,
        ["NotFound"] = 404,
        ["Conflict"] = 409,
        ["Invalid"] = 400,
        ["Error"] = 500,
        ["CriticalError"] = 500,
        ["Unavailable"] = 503,
    };

    /// <summary>
    /// Scans the handler method body for <c>Result.NotFound()</c>, <c>Result.Invalid()</c>, etc.
    /// factory method calls and returns the corresponding HTTP status codes.
    /// Also detects whether <c>Result.Created()</c> is used (returned via out parameter).
    /// Only runs when the return type involves <c>Result</c> or <c>Result&lt;T&gt;</c>.
    /// </summary>
    private static int[] DetectResultStatusCodes(IMethodSymbol handlerMethod, ITypeSymbol returnType, Compilation compilation, out bool usesResultCreated)
    {
        usesResultCreated = false;

        // Only scan if the return type involves Result/Result<T>
        var unwrapped = returnType.UnwrapTask(compilation).UnwrapNullable(compilation);

        // Unwrap tuple — check if first element is Result
        if (unwrapped is INamedTypeSymbol { IsTupleType: true } tupleType && tupleType.TupleElements.Length > 0)
            unwrapped = tupleType.TupleElements[0].Type;

        if (!unwrapped.IsResult(compilation))
            return [];

        // Get the syntax node for the method body
        var syntaxRef = handlerMethod.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
            return [];

        var syntaxNode = syntaxRef.GetSyntax();

        var detectedCodes = new HashSet<int>();

        // Walk all descendant nodes looking for invocations of Result factory methods
        foreach (var invocation in syntaxNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string? methodName = null;

            // Match: Result.NotFound(...), Result<T>.NotFound(...), or ResultStatus.NotFound style
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                methodName = memberAccess.Name.Identifier.ValueText;
            }

            if (methodName != null && ResultMethodToStatusCode.TryGetValue(methodName, out var statusCode))
            {
                detectedCodes.Add(statusCode);
            }

            // Detect Result.Created() calls separately (not an error status code)
            if (methodName == "Created")
            {
                usesResultCreated = true;
            }
        }

        return detectedCodes.OrderBy(c => c).ToArray();
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

    #region Handler Middleware Extraction

    /// <summary>
    /// Extracts middleware references from [UseMiddleware] and custom attributes deriving from UseMiddlewareAttribute.
    /// </summary>
    private static List<HandlerMiddlewareReference> ExtractHandlerMiddlewareReferences(
        INamedTypeSymbol classSymbol,
        IMethodSymbol handlerMethod,
        Compilation compilation)
    {
        var references = new List<HandlerMiddlewareReference>();

        var useMiddlewareAttr = compilation.GetTypeByMetadataName(WellKnownTypes.UseMiddlewareAttribute);
        if (useMiddlewareAttr == null)
            return references;

        // Process method-level attributes first (higher priority)
        foreach (var attr in handlerMethod.GetAttributes())
        {
            var middlewareRef = TryGetMiddlewareReference(attr, useMiddlewareAttr, isMethodLevel: true, compilation);
            if (middlewareRef != null)
                references.Add(middlewareRef.Value);
        }

        // Process class-level attributes
        foreach (var attr in classSymbol.GetAttributes())
        {
            var middlewareRef = TryGetMiddlewareReference(attr, useMiddlewareAttr, isMethodLevel: false, compilation);
            if (middlewareRef != null)
                references.Add(middlewareRef.Value);
        }

        return references;
    }

    /// <summary>
    /// Attempts to extract a middleware reference from an attribute.
    /// Supports:
    /// 1. Direct [UseMiddleware(typeof(X))] usage on handlers
    /// 2. Custom attributes that have [UseMiddleware(typeof(X))] applied to them
    /// </summary>
    private static HandlerMiddlewareReference? TryGetMiddlewareReference(
        AttributeData attr,
        INamedTypeSymbol useMiddlewareAttr,
        bool isMethodLevel,
        Compilation compilation)
    {
        if (attr.AttributeClass == null)
            return null;

        string? middlewareTypeName = null;
        int order = int.MaxValue;

        // Case 1: Direct [UseMiddleware(typeof(X))] usage
        if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, useMiddlewareAttr))
        {
            if (attr.ConstructorArguments.Length > 0 &&
                attr.ConstructorArguments[0].Value is ITypeSymbol middlewareType)
            {
                middlewareTypeName = middlewareType.ToDisplayString();
            }

            // Get Order from named argument
            var orderArg = attr.NamedArguments.FirstOrDefault(na => na.Key == "Order");
            if (orderArg.Value.Value is int orderValue)
                order = orderValue;
        }
        // Case 2: Custom attribute that has [UseMiddleware(typeof(X))] applied to it
        else
        {
            // Check if the attribute class has [UseMiddleware] applied to it
            middlewareTypeName = GetMiddlewareTypeFromAttribute(attr.AttributeClass, useMiddlewareAttr);
        }

        if (middlewareTypeName == null)
            return null;

        return new HandlerMiddlewareReference
        {
            MiddlewareTypeName = middlewareTypeName,
            Order = order,
            IsMethodLevel = isMethodLevel
        };
    }

    /// <summary>
    /// Extracts the middleware type from a custom attribute that has [UseMiddleware(typeof(X))] applied to it.
    /// </summary>
    private static string? GetMiddlewareTypeFromAttribute(INamedTypeSymbol attributeClass, INamedTypeSymbol useMiddlewareAttr)
    {
        foreach (var attr in attributeClass.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, useMiddlewareAttr) &&
                attr.ConstructorArguments.Length > 0 &&
                attr.ConstructorArguments[0].Value is ITypeSymbol middlewareType)
            {
                return middlewareType.ToDisplayString();
            }
        }

        return null;
    }

    #endregion
}
