using System;
using System.Linq;

namespace Foundatio.Mediator;

/// <summary>
/// Utility for deriving clean project names from assembly names.
/// This file is source-imported by the test project.
/// </summary>
internal static class AssemblyNameHelper
{
    /// <summary>
    /// Derives a clean project name from the assembly name for use as a suffix
    /// in generated endpoint method names. Takes the last meaningful segment,
    /// strips common suffixes like Api/Web/Module/Service/Server, and sanitizes.
    /// </summary>
    /// <example>
    /// "MyApp.Orders.Api" → "Orders"
    /// "Products.Module" → "Products"
    /// "MyWebApp" → "MyWebApp"
    /// "my-cool-api" → "my_cool_api"
    /// </example>
    internal static string DeriveProjectNameFromAssembly(string assemblyName)
    {
        // Split on dots to get segments
        var segments = assemblyName.Split('.');

        // Common suffixes to strip (case-insensitive)
        string[] stripSuffixes = ["Api", "Web", "Module", "Service", "Server", "Host", "App"];

        // Walk backwards through segments to find the first meaningful one
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            var segment = segments[i].Trim();
            if (string.IsNullOrEmpty(segment))
                continue;

            // Skip if this segment is just a common suffix
            bool isSuffix = false;
            foreach (var suffix in stripSuffixes)
            {
                if (string.Equals(segment, suffix, StringComparison.OrdinalIgnoreCase))
                {
                    isSuffix = true;
                    break;
                }
            }

            if (!isSuffix)
                return SanitizeIdentifier(segment.Replace("-", "_"));
        }

        // Fallback: use the full assembly name if all segments are suffixes
        return SanitizeIdentifier(assemblyName.Replace(".", "_").Replace("-", "_"));
    }

    private static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        var identifier = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());

        // C# identifiers cannot start with a digit, so prefix with underscore if needed
        if (identifier.Length > 0 && char.IsDigit(identifier[0]))
            return "_" + identifier;

        return identifier;
    }
}
