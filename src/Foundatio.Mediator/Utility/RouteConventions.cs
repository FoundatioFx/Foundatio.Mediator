namespace Foundatio.Mediator.Utility;

/// <summary>
/// Shared route convention logic used by both the source generator (<see cref="HandlerAnalyzer"/>)
/// and the real-time diagnostic analyzer (<see cref="MediatorInfoAnalyzer"/>).
/// All methods are pure functions operating on strings — no Roslyn symbol dependencies.
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
    /// Action verb prefixes that default to POST and generate a route suffix.
    /// </summary>
    public static readonly string[] ActionPrefixes =
    [
        "Complete", "Approve", "Cancel", "Submit", "Process",
        "Execute", "Activate", "Deactivate", "Archive", "Restore",
        "Publish", "Unpublish", "Enable", "Disable", "Reset",
        "Confirm", "Reject", "Assign", "Unassign", "Close", "Reopen",
        "Export", "Import", "Download", "Upload"
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
    /// Removes common verb prefixes from message type names, returning the entity portion.
    /// </summary>
    public static string RemoveVerbPrefix(string name)
    {
        foreach (var prefix in CrudPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
                return name.Substring(prefix.Length);
        }

        foreach (var prefix in ActionPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
                return name.Substring(prefix.Length);
        }

        return name;
    }

    /// <summary>
    /// Returns the action verb (kebab-cased) if the message name starts with an action prefix,
    /// or null for CRUD verbs.
    /// </summary>
    public static string? GetActionVerb(string messageTypeName)
    {
        foreach (var prefix in ActionPrefixes)
        {
            if (messageTypeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && messageTypeName.Length > prefix.Length)
                return prefix.ToKebabCase();
        }

        return null;
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

        if (string.IsNullOrEmpty(groupRoutePrefix))
        {
            var kebabEntity = entityName.SimplePluralize().ToKebabCase();
            if (!string.IsNullOrEmpty(kebabEntity))
                parts.Add(kebabEntity);
        }

        if (lookupSuffix != null)
            parts.Add(lookupSuffix);

        foreach (var param in routeParamNames)
            parts.Add($"{{{param}}}");

        if (actionVerb != null)
        {
            bool entityMatchesGroup = !string.IsNullOrEmpty(groupName) &&
                entityName.Length >= 2 &&
                (groupName!.StartsWith(entityName, StringComparison.OrdinalIgnoreCase) ||
                 entityName.StartsWith(groupName, StringComparison.OrdinalIgnoreCase));

            if (entityMatchesGroup || string.IsNullOrEmpty(groupRoutePrefix))
                parts.Add(actionVerb);
            else
                parts.Add(actionVerb + "-" + entityName.SimplePluralize().ToKebabCase());
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
}
