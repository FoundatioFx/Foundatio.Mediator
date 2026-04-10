namespace Foundatio.Mediator;

/// <summary>
/// Service responsible for evaluating <see cref="AuthorizationRequirements"/> against a
/// <see cref="ClaimsPrincipal"/>. Called automatically by generated handler code when
/// authorization requirements are present.
/// <para>
/// The default implementation (<see cref="DefaultHandlerAuthorizationService"/>) checks
/// authentication status and roles via <see cref="ClaimsPrincipal.IsInRole(string)"/>.
/// Users can register their own implementation to integrate with ASP.NET Core's
/// <c>IAuthorizationService</c> for named policy evaluation or any other authorization system.
/// </para>
/// </summary>
public interface IHandlerAuthorizationService
{
    /// <summary>
    /// Evaluates whether the given principal satisfies the specified authorization requirements.
    /// </summary>
    /// <param name="principal">The current user's claims principal. May be <c>null</c> if no user is authenticated.</param>
    /// <param name="requirements">The authorization requirements to evaluate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An <see cref="AuthorizationResult"/> indicating success or the type of failure.</returns>
    ValueTask<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal? principal,
        AuthorizationRequirements requirements,
        CancellationToken cancellationToken = default);
}
