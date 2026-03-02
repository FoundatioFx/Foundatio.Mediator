using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator.Models;

/// <summary>
/// Contains resolved authorization metadata for a handler, extracted at compile time
/// from [HandlerAuthorize], [HandlerAllowAnonymous], [AllowAnonymous], and assembly-level defaults.
/// </summary>
internal readonly record struct AuthorizationInfo
{
    /// <summary>
    /// Whether authorization is required for this handler.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// Whether anonymous access is explicitly allowed (overrides Required).
    /// </summary>
    public bool AllowAnonymous { get; init; }

    /// <summary>
    /// Required roles (any-of semantics).
    /// </summary>
    public EquatableArray<string> Roles { get; init; }

    /// <summary>
    /// Required policies (all must be satisfied).
    /// </summary>
    public EquatableArray<string> Policies { get; init; }

    /// <summary>
    /// Whether this handler needs authorization checks emitted in generated code.
    /// </summary>
    public bool ShouldEnforce => Required && !AllowAnonymous;

    public static AuthorizationInfo Default => new()
    {
        Required = false,
        AllowAnonymous = false,
        Roles = EquatableArray<string>.Empty,
        Policies = EquatableArray<string>.Empty
    };
}
