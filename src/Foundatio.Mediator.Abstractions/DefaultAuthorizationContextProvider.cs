using System.Security.Claims;

namespace Foundatio.Mediator;

/// <summary>
/// Default <see cref="IAuthorizationContextProvider"/> that reads the current principal
/// from <see cref="Thread.CurrentPrincipal"/>.
/// <para>
/// This works for console apps and background workers where the principal is set on the thread.
/// In ASP.NET Core apps, the source generator emits an <c>HttpContextAuthorizationContextProvider</c>
/// that reads from <c>IHttpContextAccessor.HttpContext.User</c> instead.
/// </para>
/// </summary>
public sealed class DefaultAuthorizationContextProvider : IAuthorizationContextProvider
{
    /// <inheritdoc />
    public ClaimsPrincipal? GetCurrentPrincipal() => Thread.CurrentPrincipal as ClaimsPrincipal;
}
