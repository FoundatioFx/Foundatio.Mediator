using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Scalar.AspNetCore;

namespace Microsoft.Extensions.DependencyInjection;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Registers one OpenAPI document per API version.
    /// The generated <c>ApiVersionOpenApiProvider</c> assigns endpoints to the
    /// correct version document based on <c>ApiVersionMetadata</c>.
    /// </summary>
    public static IServiceCollection AddOpenApiDocs(this IServiceCollection services, params string[] versions)
    {
        foreach (var version in versions)
        {
            var docName = version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : "v" + version;
            services.AddOpenApi(docName);
        }

        return services;
    }

    /// <summary>
    /// Maps OpenAPI endpoints and Scalar API reference for all version documents.
    /// The latest version (last in the array) is shown by default at /scalar.
    /// </summary>
    public static WebApplication MapOpenApiDocs(this WebApplication app, string title, params string[] versions)
    {
        app.MapOpenApi();

        app.MapScalarApiReference(options =>
        {
            options.WithTitle(title);
            options.SortTagsAlphabetically();
            for (int i = 0; i < versions.Length; i++)
            {
                var docName = versions[i].StartsWith("v", StringComparison.OrdinalIgnoreCase) ? versions[i] : "v" + versions[i];
                var isLatest = i == versions.Length - 1;
                options.AddDocument(docName, isDefault: isLatest);
            }
        });

        return app;
    }

    /// <summary>
    /// Adds simple cookie authentication for the sample application.
    /// Returns 401/403 JSON responses instead of redirecting to a login page.
    /// </summary>
    public static IServiceCollection AddDemoAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "ModularMonolith.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Maps demo authentication endpoints: login, logout, and current user info.
    /// Demo users: admin/admin (Admin role), user/user (User role).
    /// </summary>
    public static WebApplication MapDemoAuthEndpoints(this WebApplication app)
    {
        var demoUsers = new Dictionary<string, (string Password, string DisplayName, string Role)>(StringComparer.OrdinalIgnoreCase)
        {
            ["admin"] = ("admin", "Alice Admin", "Admin"),
            ["user"]  = ("user",  "Bob User",   "User"),
        };

        app.MapPost("/api/auth/login", async (HttpContext http, LoginRequest request) =>
        {
            if (!demoUsers.TryGetValue(request.Username, out var user) || user.Password != request.Password)
                return Results.Problem("Invalid username or password.", statusCode: 401);

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user.DisplayName),
                new(ClaimTypes.NameIdentifier, request.Username),
                new(ClaimTypes.Role, user.Role),
            };

            var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            return Results.Ok(new UserInfo(user.DisplayName, request.Username, user.Role));
        }).AllowAnonymous();

        app.MapPost("/api/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        }).AllowAnonymous();

        app.MapGet("/api/auth/me", (HttpContext http) =>
        {
            if (http.User.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            var displayName = http.User.Identity.Name ?? "unknown";
            var username = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
            var role = http.User.FindFirstValue(ClaimTypes.Role) ?? "User";

            return Results.Ok(new UserInfo(displayName, username, role));
        }).AllowAnonymous();

        return app;
    }
}

record LoginRequest(string Username, string Password);
record UserInfo(string DisplayName, string Username, string Role);
