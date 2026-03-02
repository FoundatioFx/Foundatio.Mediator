namespace Foundatio.Mediator;

/// <summary>
/// Immutable container for the authorization requirements of a handler.
/// Populated at compile time by the source generator and baked into <see cref="HandlerExecutionInfo"/>.
/// </summary>
public sealed class AuthorizationRequirements
{
    /// <summary>
    /// Default requirements: no authorization required.
    /// </summary>
    public static AuthorizationRequirements Default { get; } = new(false, Array.Empty<string>(), Array.Empty<string>(), false);

    /// <summary>
    /// Creates a new <see cref="AuthorizationRequirements"/> instance.
    /// </summary>
    /// <param name="required">Whether authorization is required for this handler.</param>
    /// <param name="roles">Role names required (any-of semantics). Empty if no role requirement.</param>
    /// <param name="policies">Policy names required (all must be satisfied). Empty if no policy requirement.</param>
    /// <param name="allowAnonymous">Whether anonymous access is explicitly allowed, overriding any other requirements.</param>
    public AuthorizationRequirements(bool required, IReadOnlyList<string> roles, IReadOnlyList<string> policies, bool allowAnonymous)
    {
        Required = required;
        Roles = roles ?? Array.Empty<string>();
        Policies = policies ?? Array.Empty<string>();
        AllowAnonymous = allowAnonymous;
    }

    /// <summary>
    /// Whether authorization is required for this handler.
    /// True when <c>[HandlerAuthorize]</c> is present or assembly-level <c>AuthorizationRequired = true</c>.
    /// </summary>
    public bool Required { get; }

    /// <summary>
    /// Role names required for this handler. Multiple roles use "any of" semantics
    /// (the user must have at least one of the specified roles).
    /// </summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// Authorization policy names required for this handler. All policies must be satisfied.
    /// </summary>
    public IReadOnlyList<string> Policies { get; }

    /// <summary>
    /// Whether anonymous access is explicitly allowed, overriding any other authorization requirements.
    /// Set when the handler or method has <c>[HandlerAllowAnonymous]</c> or <c>[AllowAnonymous]</c>.
    /// </summary>
    public bool AllowAnonymous { get; }

    /// <summary>
    /// Whether this handler has any authorization requirements that need enforcement.
    /// Returns true when <see cref="Required"/> is true and <see cref="AllowAnonymous"/> is false.
    /// </summary>
    public bool ShouldEnforce => Required && !AllowAnonymous;
}
