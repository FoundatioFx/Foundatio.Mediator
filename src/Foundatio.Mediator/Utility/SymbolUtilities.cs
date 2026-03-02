namespace Foundatio.Mediator.Utility;

/// <summary>
/// Shared helper methods for analyzing Roslyn symbols across handler analyzers and scanners.
/// </summary>
internal static class SymbolUtilities
{
    /// <summary>
    /// Method names recognized as handler entry points by convention.
    /// </summary>
    public static readonly string[] ValidHandlerMethodNames =
    [
        "Handle", "HandleAsync",
        "Handles", "HandlesAsync",
        "Consume", "ConsumeAsync",
        "Consumes", "ConsumesAsync"
    ];

    /// <summary>
    /// Returns all methods declared on <paramref name="targetSymbol"/> (and optionally its base types),
    /// de-duplicated by signature so overridden methods are only returned once.
    /// </summary>
    public static IEnumerable<IMethodSymbol> GetMethods(INamedTypeSymbol targetSymbol, bool includeBaseMethods = true)
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

    /// <summary>
    /// Builds a unique signature string for a method based on name and parameter types.
    /// </summary>
    public static string BuildMethodSignature(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
            return method.Name + "()";

        string[] parts = new string[method.Parameters.Length];
        for (int i = 0; i < method.Parameters.Length; i++)
            parts[i] = method.Parameters[i].Type.ToDisplayString();

        return method.Name + "(" + String.Join(",", parts) + ")";
    }

    /// <summary>
    /// Builds a generic constraint clause string for a type parameter (e.g. <c>where T : class, IFoo</c>).
    /// </summary>
    public static string BuildConstraintClause(ITypeParameterSymbol tp)
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

    /// <summary>
    /// Returns <c>true</c> when <paramref name="typeSymbol"/> is nested inside a generic type.
    /// </summary>
    public static bool IsNestedInGenericType(INamedTypeSymbol typeSymbol)
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
}
