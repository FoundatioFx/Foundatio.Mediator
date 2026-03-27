using System.Security.Claims;
using Common.Module.Messages;
using Common.Module.Services;
using Foundatio.Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace Common.Module.Handlers;

/// <summary>
/// Handles authentication operations (login, logout, current user).
/// These handlers use <c>HttpContext</c> as a parameter — automatically resolved from
/// <c>CallContext</c> when invoked through generated endpoints.
/// </summary>
[HandlerEndpointGroup("Auth")]
public class AuthHandler
{
    [HandlerAllowAnonymous]
    [HandlerEndpoint(Route = "login")]
    public async Task<Result<UserInfo>> HandleAsync(
        Login command,
        HttpContext httpContext,
        IDemoUserService userService,
        CancellationToken ct)
    {
        if (!userService.TryGetUser(command.Username, out var user) || user.Password != command.Password)
            return Result.Unauthorized("Invalid username or password.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.NameIdentifier, user.Username),
            new(ClaimTypes.Role, user.Role),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return new UserInfo(user.DisplayName, user.Username, user.Role);
    }

    public async Task<Result> HandleAsync(
        Logout command,
        HttpContext httpContext,
        CancellationToken ct)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Result.Ok();
    }

    [HandlerEndpoint(Route = "me")]
    public Result<UserInfo> Handle(GetCurrentUser query, HttpContext httpContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true)
            return Result.Unauthorized();

        var displayName = httpContext.User.Identity.Name ?? "unknown";
        var username = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var role = httpContext.User.FindFirstValue(ClaimTypes.Role) ?? "User";

        return new UserInfo(displayName, username, role);
    }
}
