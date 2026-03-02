using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator.Models;

/// <summary>
/// Contains parsed assembly-level endpoint defaults from [assembly: MediatorConfiguration].
/// </summary>
internal readonly record struct EndpointDefaultsInfo
{
    /// <summary>
    /// The endpoint discovery mode (None, Explicit, All).
    /// </summary>
    public string Discovery { get; init; }

    /// <summary>
    /// The global route prefix applied to all generated endpoints.
    /// </summary>
    public string? RoutePrefix { get; init; }

    /// <summary>
    /// Global endpoint filter type names (fully qualified).
    /// </summary>
    public EquatableArray<string> Filters { get; init; }

    /// <summary>
    /// Whether all handlers and endpoints require authentication by default.
    /// Populated from the renamed <c>AuthorizationRequired</c> property on <c>[assembly: MediatorConfiguration]</c>.
    /// </summary>
    public bool RequireAuth { get; init; }

    /// <summary>
    /// The default authorization policies for all handlers and endpoints.
    /// Populated from <c>AuthorizationPolicies</c> on <c>[assembly: MediatorConfiguration]</c>.
    /// </summary>
    public EquatableArray<string> Policies { get; init; }

    /// <summary>
    /// The default required roles for all handlers and endpoints.
    /// Populated from the renamed <c>AuthorizationRoles</c> property on <c>[assembly: MediatorConfiguration]</c>.
    /// </summary>
    public EquatableArray<string> Roles { get; init; }

    /// <summary>
    /// Controls how the endpoint summary is generated from the message type name.
    /// "Exact" uses the name as-is, "Spaced" splits PascalCase into words.
    /// </summary>
    public string SummaryStyle { get; init; }

    /// <summary>
    /// Whether the [assembly: MediatorConfiguration] attribute was found.
    /// </summary>
    public bool IsConfigured { get; init; }

    public static EndpointDefaultsInfo Default => new()
    {
        Discovery = "None",
        RoutePrefix = "/api",
        Filters = EquatableArray<string>.Empty,
        RequireAuth = false,
        Policies = EquatableArray<string>.Empty,
        Roles = EquatableArray<string>.Empty,
        SummaryStyle = "Exact",
        IsConfigured = false
    };
}
