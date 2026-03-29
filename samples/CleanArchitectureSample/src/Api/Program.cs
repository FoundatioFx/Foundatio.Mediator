using Common.Module;
using Foundatio.Mediator;
using Microsoft.AspNetCore.Authentication.Cookies;
using Orders.Module;
using Products.Module;
using Reports.Module;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddOpenApiDocs(ApiConstants.AllVersions);
builder.Services.AddDemoAuthentication();

// Add Foundatio.Mediator — all referenced module assemblies are auto-discovered
builder.Services.AddMediator();

// Add module services
builder.Services.AddCommonModule();
builder.Services.AddOrdersModule();
builder.Services.AddProductsModule();
builder.Services.AddReportsModule();

var app = builder.Build();

// Serve static files from the SPA
app.UseDefaultFiles();
app.MapStaticAssets();

app.MapOpenApiDocs("Modular Monolith API", ApiConstants.AllVersions);

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapDemoAuthEndpoints();

// Map module endpoints - discovers and maps all endpoint modules from referenced assemblies
app.MapMediatorEndpoints();

// SPA fallback - serves index.html for client-side routing
app.MapFallbackToFile("/index.html");

app.Run();


