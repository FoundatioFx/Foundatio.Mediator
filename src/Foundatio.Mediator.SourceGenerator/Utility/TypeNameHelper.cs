using Microsoft.CodeAnalysis;
using System.Text;

namespace Foundatio.Mediator.Utility;

internal static class TypeNameHelper
{
    /// <summary>
    /// Gets the runtime-compatible type name from an ITypeSymbol.
    /// This builds the type name properly using the Roslyn API to distinguish between
    /// namespace separators (.) and nested type separators (+).
    /// </summary>
    /// <param name="typeSymbol">The type symbol to get the name for</param>
    /// <returns>Type name compatible with Type.FullName at runtime</returns>
    public static string GetRuntimeTypeName(ITypeSymbol typeSymbol)
    {
        // Use the proper Roslyn way to build runtime type names
        var parts = new List<string>();

        // Build the type hierarchy from innermost to outermost
        var currentType = typeSymbol;
        while (currentType != null)
        {
            // Get the type name without namespace
            string typeName = currentType.Name;

            // Handle generic types
            if (currentType is INamedTypeSymbol namedType && namedType.TypeArguments.Length > 0)
            {
                typeName += "`" + namedType.TypeArguments.Length;
            }

            parts.Insert(0, typeName);

            // Move to containing type (for nested types)
            currentType = currentType.ContainingType;
        }

        // Build the full type name
        string namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString() ?? String.Empty;
        if (!string.IsNullOrEmpty(namespaceName) && namespaceName != "<global namespace>")
        {
            // Namespace parts use dots
            string typePart = string.Join("+", parts); // Nested types use +
            return namespaceName + "." + typePart;
        }
        else
        {
            // No namespace, just the type parts
            return string.Join("+", parts);
        }
    }

    /// <summary>
    /// For string-based type names from ToDisplayString(), convert nested type separators.
    /// This handles the case where ITypeSymbol.ToDisplayString() uses dots for nested types
    /// but Type.FullName uses + for nested types.
    /// </summary>
    /// <param name="displayTypeName">Type name from ITypeSymbol.ToDisplayString()</param>
    /// <returns>Type name compatible with Type.FullName for runtime lookup</returns>
    public static string ConvertToRuntimeTypeName(string displayTypeName)
    {
        // For most cases, ITypeSymbol.ToDisplayString() produces the correct format
        // The main exception is nested types where dots should become +

        // Conservative heuristic: only convert if we have at least 5 parts suggesting deep nesting
        // like "Namespace1.Namespace2.Namespace3.OuterClass.InnerClass"
        var parts = displayTypeName.Split('.');
        if (parts.Length < 5)
        {
            // For simple cases, assume no nested types and return as-is
            return displayTypeName;
        }

        // For complex cases, apply the nested type logic
        int lastDotIndex = displayTypeName.LastIndexOf('.');
        if (lastDotIndex <= 0 || lastDotIndex == displayTypeName.Length - 1)
        {
            return displayTypeName;
        }

        // Check if the part after the last dot looks like a type name (starts with uppercase)
        string lastPart = displayTypeName.Substring(lastDotIndex + 1);
        if (lastPart.Length > 0 && char.IsUpper(lastPart[0]))
        {
            // Check if the part before the last dot also looks like it could contain a type name
            string beforeLastDot = displayTypeName.Substring(0, lastDotIndex);
            int secondLastDotIndex = beforeLastDot.LastIndexOf('.');

            if (secondLastDotIndex >= 0)
            {
                string potentialTypeName = beforeLastDot.Substring(secondLastDotIndex + 1);
                // If the potential type name starts with uppercase, treat the last dot as a nested type separator
                if (potentialTypeName.Length > 0 && char.IsUpper(potentialTypeName[0]))
                {
                    return beforeLastDot + "+" + lastPart;
                }
            }
        }

        // No nested type pattern detected, return as-is
        return displayTypeName;
    }

    /// <summary>
    /// Gets the simple type name from a full type name, handling both . and + separators.
    /// This is useful for generating clean class names in code generation.
    /// </summary>
    /// <param name="fullTypeName">The full type name including namespace and nested type separators</param>
    /// <returns>Simple type name suitable for use as a class name</returns>
    public static string GetSimpleTypeName(string fullTypeName)
    {
        // Get the last part of the type name, handling both . and + separators
        int lastDotIndex = fullTypeName.LastIndexOf('.');
        int lastPlusIndex = fullTypeName.LastIndexOf('+');
        int lastSeparatorIndex = Math.Max(lastDotIndex, lastPlusIndex);

        string simpleName = lastSeparatorIndex >= 0
            ? fullTypeName.Substring(lastSeparatorIndex + 1)
            : fullTypeName;

        // Clean up the name for use as a class name
        return simpleName.Replace("<", "_").Replace(">", "_").Replace(",", "_").Replace("+", "_");
    }
}
