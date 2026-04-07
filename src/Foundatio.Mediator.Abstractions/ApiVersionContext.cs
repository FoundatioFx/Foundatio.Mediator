using System.Globalization;

namespace Foundatio.Mediator;

/// <summary>
/// Default implementation of <see cref="IApiVersionContext"/>. Registered as a scoped service
/// and populated by the generated <c>ApiVersionMatcherPolicy</c> that reads the API version request header.
/// </summary>
public sealed class ApiVersionContext : IApiVersionContext
{
    /// <inheritdoc />
    public string Current { get; set; } = "";

    /// <inheritdoc />
    public bool IsBefore(string version) => Compare(Current, version) < 0;

    /// <inheritdoc />
    public bool IsAtLeast(string version) => Compare(Current, version) >= 0;

    /// <inheritdoc />
    public bool Is(string version) => string.Equals(Current, version, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Compares two version strings. Supports integer ("1", "2"), semantic version ("1.0", "1.10"),
    /// ISO 8601 date ("2024-01-15"), and arbitrary strings. Falls back to ordinal string comparison.
    /// </summary>
    internal static int Compare(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return 0;

        // Try integer comparison (simple versions like "1", "2")
        if (int.TryParse(a, out var intA) && int.TryParse(b, out var intB))
            return intA.CompareTo(intB);

        // Try System.Version comparison (handles "1.0", "1.10", "2.1.3" correctly)
        if (Version.TryParse(a, out var verA) && Version.TryParse(b, out var verB))
            return verA.CompareTo(verB);

        // Try ISO 8601 date comparison (culture-invariant)
        if (DateTime.TryParseExact(a, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateA)
            && DateTime.TryParseExact(b, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateB))
            return dateA.CompareTo(dateB);

        // Fallback: ordinal string comparison
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
