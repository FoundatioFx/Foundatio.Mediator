namespace Foundatio.Mediator;

/// <summary>
/// Represents the result of a handler authorization check.
/// </summary>
public sealed class AuthorizationResult
{
    private static readonly AuthorizationResult _success = new(true, null, false);

    private AuthorizationResult(bool succeeded, string? failureReason, bool isForbidden)
    {
        Succeeded = succeeded;
        FailureReason = failureReason;
        IsForbidden = isForbidden;
    }

    /// <summary>
    /// Whether authorization succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// A human-readable reason for the authorization failure.
    /// Null when authorization succeeded.
    /// </summary>
    public string? FailureReason { get; }

    /// <summary>
    /// Whether the failure is a "forbidden" (403) vs "unauthorized" (401).
    /// When false, the user is not authenticated (or not present).
    /// When true, the user is authenticated but lacks the required role or policy.
    /// Only meaningful when <see cref="Succeeded"/> is false.
    /// </summary>
    public bool IsForbidden { get; }

    /// <summary>
    /// Creates a successful authorization result.
    /// </summary>
    public static AuthorizationResult Success() => _success;

    /// <summary>
    /// Creates an unauthorized (401) result — the user is not authenticated.
    /// </summary>
    /// <param name="reason">Optional reason for the failure.</param>
    public static AuthorizationResult Unauthorized(string? reason = null) =>
        new(false, reason ?? "Authentication is required.", false);

    /// <summary>
    /// Creates a forbidden (403) result — the user is authenticated but lacks required permissions.
    /// </summary>
    /// <param name="reason">Optional reason for the failure.</param>
    public static AuthorizationResult Forbidden(string? reason = null) =>
        new(false, reason ?? "Access is denied.", true);
}
