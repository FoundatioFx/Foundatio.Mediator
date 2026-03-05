using System.Security.Claims;
using Common.Module;
using Foundatio.Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Orders.Module;
using Products.Module;
using Reports.Module;
using Scalar.AspNetCore;

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

app.UseAuthentication();
app.UseAuthorization();

// --- Demo auth endpoints (hardcoded users for the sample) ---

// Demo users: admin / admin, user / user
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

// Map module endpoints - discovers and maps all endpoint modules from referenced assemblies
// All generated endpoints now require authentication via [assembly: MediatorConfiguration(AuthorizationRequired = true)]
// Handlers marked with [AllowAnonymous] (e.g., HealthHandler) opt out of auth
app.MapMediatorEndpoints();

// SPA fallback - serves index.html for client-side routing
app.MapFallbackToFile("/index.html");

app.Run();

// --- DTOs for auth endpoints ---

record LoginRequest(string Username, string Password);
record UserInfo(string DisplayName, string Username, string Role);


