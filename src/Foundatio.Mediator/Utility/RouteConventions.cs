namespace Foundatio.Mediator.Utility;

/// <summary>
/// Shared route convention logic used by both the source generator (<see cref="HandlerAnalyzer"/>)
/// and the real-time diagnostic analyzer (<see cref="MediatorInfoAnalyzer"/>).
/// All methods are pure functions operating on strings — no Roslyn symbol dependencies.
///
/// <para><b>Route generation algorithm (4 steps):</b></para>
/// <list type="number">
///   <item><b>Mode</b> — single-message mode (1 method, class name matches message) or group mode
///     (multiple methods or class name ≠ message). Group mode auto-derives a route prefix from the
///     handler class (e.g., <c>OrderHandler</c> → <c>/orders</c>).</item>
///   <item><b>Entity</b> — in group mode, comes from the class name (<c>OrderHandler</c> → "Order");
///     in single-message mode, extracted from the message name after stripping the verb prefix
///     (<c>GetOrder</c> → "Order").</item>
///   <item><b>HTTP method</b> — CRUD prefixes map to methods (<c>Get</c> → GET, <c>Create</c> → POST,
///     <c>Update</c> → PUT, <c>Delete</c> → DELETE, <c>Patch</c> → PATCH). Everything else → POST.</item>
///   <item><b>Route</b> — strip the entity from the message name; whatever remains is the action verb
///     suffix (e.g., <c>CompleteTodo</c> − "Todo" = "complete" → <c>/todos/{id}/complete</c>).
///     CRUD verbs produce no suffix. The entity is pluralized and kebab-cased.</item>
/// </list>
/// </summary>
internal static class RouteConventions
{
    /// <summary>
    /// All CRUD verb prefixes that map to specific HTTP methods.
    /// </summary>
    public static readonly string[] CrudPrefixes =
    [
        "Get", "Find", "Search", "List", "Query",
        "Create", "Add", "New",
        "Update", "Edit", "Modify", "Change", "Set",
        "Delete", "Remove",
        "Patch"
    ];

    /// <summary>
    /// Common suffixes stripped from entity names to normalize routes.
    /// Ordered longest-first so "Details" is checked before "Detail".
    /// </summary>
    public static readonly string[] EntitySuffixes =
    [
        "Paginated", "Details", "Detail", "Summary", "ById", "Paged", "Stream", "List"
    ];

    /// <summary>
    /// Infers the HTTP method from a message type name based on verb prefixes.
    /// </summary>
    public static string InferHttpMethod(string messageTypeName)
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
    /// Removes the verb prefix from a message type name, returning the entity portion.
    /// First tries known CRUD prefixes (Get, Create, Update, Delete, etc.).
    /// For non-CRUD messages, splits at the first PascalCase word boundary to separate
    /// the action verb from the entity (e.g., "CompleteTodo" → "Todo").
    /// </summary>
    public static string RemoveVerbPrefix(string name)
    {
        foreach (var prefix in CrudPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
                return name.Substring(prefix.Length);
        }

        // For non-CRUD messages, split at the first PascalCase word boundary
        // to separate the action verb from the entity (e.g., "CompleteTodo" → "Todo").
        // A boundary is a transition from a non-uppercase character to an uppercase one.
        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                return name.Substring(i);
        }

        return name;
    }

    /// <summary>
    /// Returns the action verb (kebab-cased) if the message name has a non-CRUD verb prefix,
    /// or null for CRUD verbs and single-word messages.
    /// Uses the handler class entity context to detect the verb dynamically by splitting
    /// at the first PascalCase word boundary (e.g., "CompleteTodo" → "complete").
    /// </summary>
    public static string? GetActionVerb(string messageTypeName)
    {
        // CRUD prefixes map to HTTP methods, not action verbs
        foreach (var prefix in CrudPrefixes)
        {
            if (messageTypeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && messageTypeName.Length > prefix.Length)
                return null;
        }

        // For non-CRUD messages, split at the first PascalCase word boundary.
        // The first word is the action verb (e.g., "CompleteTodo" → "complete").
        // A boundary is a transition from a non-uppercase character to an uppercase one.
        for (int i = 1; i < messageTypeName.Length; i++)
        {
            if (char.IsUpper(messageTypeName[i]) && !char.IsUpper(messageTypeName[i - 1]))
                return messageTypeName.Substring(0, i).ToKebabCase();
        }

        return null; // Single word or no PascalCase boundary — bare action, not an action verb
    }

    /// <summary>
    /// Normalizes an entity name by removing common qualifiers that break route grouping.
    /// Returns the normalized entity name and an optional route suffix for lookup patterns.
    /// </summary>
    public static (string entityName, string? routeSuffix) NormalizeEntityName(string entityName)
    {
        if (string.IsNullOrEmpty(entityName))
            return (entityName, null);

        // Strip leading "All" prefix (e.g., AllTodos → Todos)
        if (entityName.StartsWith("All", StringComparison.Ordinal) && entityName.Length > 3 && char.IsUpper(entityName[3]))
            entityName = entityName.Substring(3);

        // Strip known suffixes (e.g., TodoById → Todo, OrderDetails → Order)
        foreach (var suffix in EntitySuffixes)
        {
            if (entityName.EndsWith(suffix, StringComparison.Ordinal) && entityName.Length > suffix.Length)
            {
                entityName = entityName.Substring(0, entityName.Length - suffix.Length);
                return (entityName, null);
            }
        }

        // Strip With<Feature> suffix entirely (e.g., TodoItemsWithPagination → TodoItems)
        var withIndex = entityName.IndexOf("With", StringComparison.Ordinal);
        if (withIndex > 0 && withIndex + 4 < entityName.Length && char.IsUpper(entityName[withIndex + 4]))
        {
            entityName = entityName.Substring(0, withIndex);
            return (entityName, null);
        }

        // Detect Count suffix (e.g., OrderCount → entity "Order", suffix "count")
        if (entityName.EndsWith("Count", StringComparison.Ordinal) && entityName.Length > 5)
        {
            entityName = entityName.Substring(0, entityName.Length - 5);
            return (entityName, "count");
        }

        // Detect For<Entity>/From<Entity>/By<Property> patterns
        foreach (var keyword in new[] { "For", "From", "By" })
        {
            var idx = entityName.IndexOf(keyword, StringComparison.Ordinal);
            if (idx > 0 && idx + keyword.Length < entityName.Length && char.IsUpper(entityName[idx + keyword.Length]))
            {
                var suffix = entityName.Substring(idx);
                entityName = entityName.Substring(0, idx);
                return (entityName, suffix.ToKebabCase());
            }
        }

        return (entityName, null);
    }

    /// <summary>
    /// Checks whether a PascalCase name consists of a single word (no internal uppercase boundaries).
    /// E.g., "Logout" → true, "GetWidget" → false.
    /// </summary>
    public static bool IsSingleWord(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length < 2)
            return true;

        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks whether a suffix looks like a version indicator (e.g., "V1", "V2", "V10").
    /// Used to avoid creating sub-routes for versioned message types like GetWidgetV2.
    /// </summary>
    private static bool IsVersionSuffix(string suffix)
    {
        if (suffix.Length < 2)
            return false;

        if (suffix[0] is not ('V' or 'v'))
            return false;

        for (int i = 1; i < suffix.Length; i++)
        {
            if (!char.IsDigit(suffix[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts the entity prefix from a handler class name by stripping the Handler/Consumer suffix.
    /// Returns null if the name doesn't end with a recognized suffix.
    /// </summary>
    public static string? GetHandlerPrefix(string handlerClassName)
    {
        if (handlerClassName.EndsWith("Handler", StringComparison.Ordinal) && handlerClassName.Length > "Handler".Length)
            return handlerClassName.Substring(0, handlerClassName.Length - "Handler".Length);
        if (handlerClassName.EndsWith("Consumer", StringComparison.Ordinal) && handlerClassName.Length > "Consumer".Length)
            return handlerClassName.Substring(0, handlerClassName.Length - "Consumer".Length);
        return null;
    }

    /// <summary>
    /// Determines whether this handler should use "message-only" mode (no endpoint group).
    /// Message-only mode triggers when the handler class has exactly one handler method
    /// AND the class name (minus Handler/Consumer suffix) matches the message type name.
    /// In this mode, the route is derived entirely from the message name.
    /// </summary>
    public static bool IsMessageOnlyMode(string handlerClassName, string messageTypeName, int handlerMethodCount)
    {
        if (handlerMethodCount != 1)
            return false;

        var prefix = GetHandlerPrefix(handlerClassName);
        return prefix != null && string.Equals(prefix, messageTypeName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Auto-derives an endpoint group name and route prefix from the handler class name.
    /// Used for multi-handler classes (or single-handler classes where the name doesn't match the message).
    /// The group name is the pluralized class prefix (e.g., "OrderHandler" → "Orders"),
    /// and the route prefix is the kebab-cased version (e.g., "orders").
    /// Returns null values if the class doesn't have a recognized Handler/Consumer suffix.
    /// </summary>
    public static (string? groupName, string? groupRoutePrefix) DeriveGroupFromHandlerClass(string handlerClassName)
    {
        var prefix = GetHandlerPrefix(handlerClassName);
        if (string.IsNullOrEmpty(prefix))
            return (null, null);

        var pluralized = prefix!.SimplePluralize();
        return (pluralized, pluralized.ToKebabCase());
    }

    /// <summary>
    /// Generates a route template from message name and parameters.
    /// This generates the relative route (what goes after the group prefix).
    /// </summary>
    public static string GenerateRoute(
        string messageTypeName,
        string? groupRoutePrefix,
        string? groupName,
        string[] routeParamNames,
        string httpMethod,
        string? actionVerb = null,
        string? handlerClassName = null)
    {
        var parts = new System.Collections.Generic.List<string>();
        var afterPrefix = RemoveVerbPrefix(messageTypeName);
        var (entityName, lookupSuffix) = NormalizeEntityName(afterPrefix);

        // Single-word message names with no recognized verb prefix (e.g., "Logout", "Login")
        // are treated as bare actions rather than REST entities. Use the handler class prefix
        // as the group and the message name as the action segment (not pluralized).
        bool isBareAction = afterPrefix == messageTypeName && IsSingleWord(messageTypeName) && actionVerb == null;
        if (isBareAction)
        {
            if (string.IsNullOrEmpty(groupRoutePrefix) && !string.IsNullOrEmpty(handlerClassName))
            {
                var prefix = GetHandlerPrefix(handlerClassName!);
                if (!string.IsNullOrEmpty(prefix))
                {
                    var kebabPrefix = prefix!.ToKebabCase();
                    var kebabMessage = messageTypeName.ToKebabCase();
                    // Avoid duplication when handler prefix matches message name (e.g., PingHandler + Ping)
                    if (!string.Equals(kebabPrefix, kebabMessage, StringComparison.OrdinalIgnoreCase))
                        parts.Add(kebabPrefix);
                }
            }

            parts.Add(messageTypeName.ToKebabCase());

            foreach (var param in routeParamNames)
                parts.Add($"{{{param}}}");

            if (parts.Count == 0)
                return "/";

            return "/" + string.Join("/", parts.Where(p => !string.IsNullOrEmpty(p)));
        }

        // Determine if the entity derived from the message matches the auto-derived group.
        // When it matches, the group prefix already represents the entity, so we omit it from the route.
        // When it doesn't match, include it as a sub-route within the group.
        var handlerPrefix = !string.IsNullOrEmpty(handlerClassName) ? GetHandlerPrefix(handlerClassName!) : null;
        bool entityMatchesGroup = false;
        string? entitySubRoute = null;

        if (!string.IsNullOrEmpty(groupRoutePrefix) && !string.IsNullOrEmpty(entityName))
        {
            var pluralEntity = entityName.SimplePluralize();
            var pluralPrefix = handlerPrefix?.SimplePluralize() ?? groupName;

            entityMatchesGroup = string.Equals(pluralEntity, pluralPrefix, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(entityName, handlerPrefix, StringComparison.OrdinalIgnoreCase);

            if (!entityMatchesGroup && handlerPrefix != null &&
                entityName.StartsWith(handlerPrefix, StringComparison.OrdinalIgnoreCase) &&
                entityName.Length > handlerPrefix.Length &&
                char.IsUpper(entityName[handlerPrefix.Length]))
            {
                var remainder = entityName.Substring(handlerPrefix.Length);
                // Version-like suffixes (V1, V2, etc.) should not create sub-routes —
                // they differentiate message types for header-based version dispatch.
                if (IsVersionSuffix(remainder))
                {
                    entityMatchesGroup = true;
                }
                else
                {
                    // Sub-entity: e.g., "TodoItems" in "Todo" group → add "items" sub-route
                    entityMatchesGroup = true;
                    entitySubRoute = remainder.SimplePluralize().ToKebabCase();
                }
            }
        }

        if (string.IsNullOrEmpty(groupRoutePrefix))
        {
            var kebabEntity = entityName.SimplePluralize().ToKebabCase();
            if (!string.IsNullOrEmpty(kebabEntity))
                parts.Add(kebabEntity);
        }
        else if (!entityMatchesGroup && !string.IsNullOrEmpty(entityName))
        {
            // Entity doesn't match group — include it as a singular sub-resource (not pluralized)
            var kebabEntity = entityName.ToKebabCase();
            if (!string.IsNullOrEmpty(kebabEntity))
                parts.Add(kebabEntity);
        }
        else if (entitySubRoute != null)
        {
            parts.Add(entitySubRoute);
        }

        if (lookupSuffix != null)
            parts.Add(lookupSuffix);

        foreach (var param in routeParamNames)
            parts.Add($"{{{param}}}");

        if (actionVerb != null)
        {
            if (entityMatchesGroup || string.IsNullOrEmpty(groupRoutePrefix))
                parts.Add(actionVerb);
            else
                parts.Add(actionVerb + "-" + entityName.ToKebabCase());
        }

        if (parts.Count == 0)
            return "/";

        return "/" + string.Join("/", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>
    /// Computes the full display route by joining global prefix, group prefix, and endpoint route.
    /// </summary>
    public static string ComputeFullDisplayRoute(string? globalPrefix, string groupPrefix, string endpointRoute, bool groupBypassGlobalPrefix, bool routeBypassPrefixes)
    {
        string result;
        if (routeBypassPrefixes)
            result = endpointRoute;
        else if (groupBypassGlobalPrefix)
            result = JoinRouteParts(groupPrefix, endpointRoute);
        else
            result = JoinRouteParts(globalPrefix ?? "", JoinRouteParts(groupPrefix, endpointRoute));

        if (string.IsNullOrEmpty(result))
            return "/";
        if (!result.StartsWith("/"))
            result = "/" + result;
        return result;
    }

    /// <summary>
    /// Joins two route path segments with a single /.
    /// </summary>
    public static string JoinRouteParts(string a, string b)
    {
        a = a.TrimEnd('/');
        b = b.TrimStart('/');
        if (string.IsNullOrEmpty(a)) return b;
        if (string.IsNullOrEmpty(b)) return a;
        return a + "/" + b;
    }

    /// <summary>
    /// Input for the shared endpoint route computation.
    /// Both HandlerAnalyzer (source generator) and MediatorInfoAnalyzer (diagnostic) extract
    /// attribute values and call <see cref="RouteConventions.ComputeEndpointRouteInfo"/> with this data.
    /// </summary>
    internal readonly record struct EndpointRouteInput
    {
        /// <summary>Handler class name (e.g., "OrderHandler").</summary>
        public string HandlerClassName { get; init; }

        /// <summary>Message type name (e.g., "GetOrder").</summary>
        public string MessageTypeName { get; init; }

        /// <summary>Number of handler methods on the class.</summary>
        public int HandlerMethodCount { get; init; }

        /// <summary>Route parameter names (camelCase) extracted from the message type (ID properties, [FromRoute]).</summary>
        public string[] RouteParamNames { get; init; }

        /// <summary>Global route prefix from [assembly: MediatorConfiguration(EndpointRoutePrefix = "...")].</summary>
        public string GlobalRoutePrefix { get; init; }

        // ── From [HandlerEndpointGroup] ────────────────────────────

        /// <summary>Whether [HandlerEndpointGroup] is present on the class.</summary>
        public bool HasGroupAttribute { get; init; }

        /// <summary>Group name from [HandlerEndpointGroup("Name")] constructor arg or Name property.</summary>
        public string? GroupName { get; init; }

        /// <summary>RoutePrefix from [HandlerEndpointGroup(RoutePrefix = "...")].</summary>
        public string? GroupRoutePrefix { get; init; }

        // ── From [HandlerEndpoint] ─────────────────────────────────

        /// <summary>HTTP method override (1=GET,2=POST,3=PUT,4=DELETE,5=PATCH, 0=auto).</summary>
        public int HttpMethodEnum { get; init; }

        /// <summary>Explicit route from [HandlerEndpoint(Route = "...")].</summary>
        public string? ExplicitRoute { get; init; }

        /// <summary>Whether this is a streaming handler (IAsyncEnumerable return type).</summary>
        public bool IsStreaming { get; init; }
    }

    /// <summary>
    /// Output from the shared endpoint route computation.
    /// </summary>
    internal readonly record struct EndpointRouteOutput
    {
        public string HttpMethod { get; init; }
        public string FullRoute { get; init; }
        public string Route { get; init; }
        public string? GroupName { get; init; }
        public string? GroupRoutePrefix { get; init; }
        public bool GroupBypassGlobalPrefix { get; init; }
        public bool RouteBypassPrefixes { get; init; }
        public bool HasExplicitRoute { get; init; }
    }

    /// <summary>
    /// Shared endpoint route computation used by both the source generator (HandlerAnalyzer)
    /// and the diagnostic analyzer (MediatorInfoAnalyzer). This is the single source of truth
    /// for how handler methods map to endpoint routes.
    /// </summary>
    internal static EndpointRouteOutput ComputeEndpointRouteInfo(EndpointRouteInput input)
    {
        // ── HTTP method ────────────────────────────────────────────
        var httpMethod = input.HttpMethodEnum switch
        {
            1 => "GET",
            2 => "POST",
            3 => "PUT",
            4 => "DELETE",
            5 => "PATCH",
            _ => input.IsStreaming ? "GET" : InferHttpMethod(input.MessageTypeName)
        };

        // ── Group info from [HandlerEndpointGroup] ─────────────────
        string? groupName = null;
        string? groupRoutePrefix = null;

        if (input.HasGroupAttribute)
        {
            groupName = input.GroupName;

            // Auto-derive group name from handler class when not specified (pluralized, like auto-derive)
            if (string.IsNullOrEmpty(groupName))
            {
                var prefix = GetHandlerPrefix(input.HandlerClassName);
                if (!string.IsNullOrEmpty(prefix))
                    groupName = prefix!.SimplePluralize();
            }

            groupRoutePrefix = input.GroupRoutePrefix;
            if (string.IsNullOrEmpty(groupRoutePrefix) && !string.IsNullOrEmpty(groupName))
                groupRoutePrefix = groupName!.ToKebabCase();
        }

        // ── Absolute vs relative group prefix ──────────────────────
        bool groupBypassGlobalPrefix = false;
        if (!string.IsNullOrEmpty(groupRoutePrefix) && groupRoutePrefix!.StartsWith("/", StringComparison.Ordinal))
        {
            groupBypassGlobalPrefix = true;
        }
        else if (!string.IsNullOrEmpty(groupRoutePrefix))
        {
            groupRoutePrefix = groupRoutePrefix!.TrimStart('/');
        }

        // ── Auto-derive group when no [HandlerEndpointGroup] ───────
        if (!input.HasGroupAttribute)
        {
            bool isMessageOnlyMode = IsMessageOnlyMode(input.HandlerClassName, input.MessageTypeName, input.HandlerMethodCount);
            if (!isMessageOnlyMode)
            {
                var (derivedGroupName, derivedGroupRoutePrefix) = DeriveGroupFromHandlerClass(input.HandlerClassName);
                if (derivedGroupName != null)
                {
                    groupName = derivedGroupName;
                    groupRoutePrefix = derivedGroupRoutePrefix;
                }
            }
        }

        // ── Explicit route ─────────────────────────────────────────
        var explicitRoute = input.ExplicitRoute;
        bool hasExplicitRoute = !string.IsNullOrEmpty(explicitRoute);
        bool routeBypassPrefixes = false;
        if (explicitRoute != null && explicitRoute.StartsWith("/", StringComparison.Ordinal))
            routeBypassPrefixes = true;

        // Auto-derive group when explicit relative route is set but no group was derived yet.
        if (string.IsNullOrEmpty(groupName) && hasExplicitRoute && !routeBypassPrefixes)
        {
            var handlerPrefix = GetHandlerPrefix(input.HandlerClassName);
            if (!string.IsNullOrEmpty(handlerPrefix))
            {
                groupName = handlerPrefix;
                groupRoutePrefix ??= handlerPrefix!.ToKebabCase();
            }
        }

        // ── Route generation ───────────────────────────────────────
        string route;
        if (hasExplicitRoute)
        {
            route = explicitRoute!;
        }
        else
        {
            var actionVerb = GetActionVerb(input.MessageTypeName);
            route = GenerateRoute(input.MessageTypeName, groupRoutePrefix, groupName,
                input.RouteParamNames, httpMethod, actionVerb, input.HandlerClassName);
        }

        // ── Full display route ─────────────────────────────────────
        var fullRoute = ComputeFullDisplayRoute(
            input.GlobalRoutePrefix, groupRoutePrefix ?? "", route,
            groupBypassGlobalPrefix, routeBypassPrefixes);

        return new EndpointRouteOutput
        {
            HttpMethod = httpMethod,
            FullRoute = fullRoute,
            Route = route,
            GroupName = groupName,
            GroupRoutePrefix = groupRoutePrefix,
            GroupBypassGlobalPrefix = groupBypassGlobalPrefix,
            RouteBypassPrefixes = routeBypassPrefixes,
            HasExplicitRoute = hasExplicitRoute,
        };
    }
}
