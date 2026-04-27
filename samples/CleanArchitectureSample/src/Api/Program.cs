using Common.Module;
using Foundatio.Mediator;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Orders.Module;
using Products.Module;
using Reports.Module;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddOpenApi();

// Simple cookie authentication for the sample
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ModularMonolith.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // Return 401 JSON instead of redirecting to a login page
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
builder.Services.AddAuthorization();

// Rate limiting policies — applied to endpoints via the [RateLimited] attribute
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Default policy: 10 requests per 10-second window
    options.AddFixedWindowLimiter("default", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromSeconds(10);
        limiter.QueueLimit = 0;
    });

    // Strict policy: 3 requests per 30-second window (for write operations)
    options.AddFixedWindowLimiter("strict", limiter =>
    {
        limiter.PermitLimit = 3;
        limiter.Window = TimeSpan.FromSeconds(30);
        limiter.QueueLimit = 0;
    });
});

// Add Foundatio.Mediator — all referenced module assemblies are auto-discovered
builder.Services.AddMediator();

// Add module services
// Order matters: Common.Module provides cross-cutting services that other modules may depend on
builder.Services.AddCommonModule();
builder.Services.AddOrdersModule();
builder.Services.AddProductsModule();
builder.Services.AddReportsModule();

// Cross-module event handlers (AuditEventHandler, NotificationEventHandler) are now
// in Common.Module and will be discovered automatically via the source generator

var app = builder.Build();

// Serve static files from the SPA
app.UseDefaultFiles();
app.MapStaticAssets();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Map module endpoints - discovers and maps all endpoint modules from referenced assemblies
app.MapMediatorEndpoints();

// SPA fallback - serves index.html for client-side routing
app.MapFallbackToFile("/index.html");

app.Run();


