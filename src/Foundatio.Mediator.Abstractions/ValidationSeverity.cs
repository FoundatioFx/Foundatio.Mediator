namespace Foundatio.Mediator;

/// <summary>
/// Represents the severity level of a validation error.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// An error that prevents the operation from completing.
    /// </summary>
    Error,

    /// <summary>
    /// A warning that doesn't prevent the operation but should be noted.
    /// </summary>
    Warning,

    /// <summary>
    /// Informational message that doesn't affect the operation.
    /// </summary>
    Info
}
