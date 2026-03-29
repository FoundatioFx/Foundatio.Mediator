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

}

