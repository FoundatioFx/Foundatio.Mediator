namespace Foundatio.Mediator;

internal static class TypeNameResolver
{
    public static IEnumerable<string> GetLookupNames(Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fullName = type.FullName;
        if (!string.IsNullOrWhiteSpace(fullName) && seen.Add(fullName))
            yield return fullName;

        var displayName = fullName?.Replace('+', '.');
        if (displayName is { Length: > 0 })
        {
            if (seen.Add(displayName!))
                yield return displayName;
        }
    }

    public static IEnumerable<string> GetLookupNames(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            yield break;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (seen.Add(typeName!))
            yield return typeName!;

        var resolvedType = Resolve(typeName);
        var resolvedFullName = resolvedType?.FullName;
        if (resolvedFullName is { Length: > 0 })
        {
            if (seen.Add(resolvedFullName!))
                yield return resolvedFullName;
        }
    }

    public static bool Matches(Type type, string? typeName)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        if (string.Equals(type.FullName, typeName, StringComparison.Ordinal))
            return true;

        return Resolve(typeName) == type;
    }

    public static Type? Resolve(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        var resolvedTypeName = typeName!;
        var type = TryResolve(resolvedTypeName, assembly: null);
        if (type != null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = TryResolve(resolvedTypeName, assembly);
            if (type != null)
                return type;
        }

        return null;
    }

    private static Type? TryResolve(string typeName, Assembly? assembly)
    {
        var type = assembly == null
            ? Type.GetType(typeName, throwOnError: false)
            : assembly.GetType(typeName, throwOnError: false);
        if (type != null)
            return type;

        return TryResolveNestedTypeName(typeName, assembly);
    }

    private static Type? TryResolveNestedTypeName(string typeName, Assembly? assembly)
    {
        var dotPositions = new List<int>();
        for (int i = 0; i < typeName.Length; i++)
        {
            if (typeName[i] == '.')
                dotPositions.Add(i);
        }

        if (dotPositions.Count == 0)
            return null;

        for (int start = dotPositions.Count - 1; start >= 0; start--)
        {
            var chars = typeName.ToCharArray();
            for (int i = start; i < dotPositions.Count; i++)
                chars[dotPositions[i]] = '+';

            var candidate = new string(chars);
            var resolved = assembly == null
                ? Type.GetType(candidate, throwOnError: false)
                : assembly.GetType(candidate, throwOnError: false);

            if (resolved != null)
                return resolved;
        }

        return null;
    }
}
