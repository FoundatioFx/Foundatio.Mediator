namespace Common.Module;

/// <summary>
/// Centralized API version constants shared across all modules.
/// Use <see cref="V1"/> and <see cref="V2"/> in attribute arguments (compile-time constants)
/// and <see cref="AllVersions"/> for runtime configuration (e.g. OpenAPI document generation).
/// </summary>
public static class ApiConstants
{
    public const string VersionHeader = "Api-Version";

    public const string V1 = "2025-01-15";
    public const string V2 = "2025-06-01";

    /// <summary>All declared API versions, ordered oldest to newest.</summary>
    public static readonly string[] AllVersions = [V1, V2];
}
