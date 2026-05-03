namespace Foundatio.Mediator.Utility;

internal static class NamespacePatternMatcher
{
    public static bool IsExcluded(string handlerNamespace, EquatableArray<string> patterns)
    {
        if (patterns.Length == 0)
            return false;

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            if (Matches(handlerNamespace, pattern))
                return true;
        }

        return false;
    }

    private static bool Matches(string handlerNamespace, string pattern)
    {
        if (pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = pattern.Substring(0, pattern.Length - 2);
            if (prefix.Length == 0)
                return false;

            return handlerNamespace.Equals(prefix, StringComparison.Ordinal)
                || handlerNamespace.StartsWith(prefix + ".", StringComparison.Ordinal);
        }

        return handlerNamespace.Equals(pattern, StringComparison.Ordinal);
    }
}
