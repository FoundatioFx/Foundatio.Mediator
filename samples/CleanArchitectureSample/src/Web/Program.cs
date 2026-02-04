using Common.Module;
using Common.Module.Events;
using Foundatio.Mediator;
using Orders.Module;
using Orders.Module.Messages;
using Products.Module;
using Products.Module.Messages;
using Reports.Module;
using Reports.Module.Messages;
using Scalar.AspNetCore;
using Web.Handlers;
using Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSignalR();

// Add Foundatio.Mediator with assemblies from all modules
builder.Services.AddMediator(c =>
{
    c.SetMediatorLifetime(ServiceLifetime.Scoped);
    c.AddAssembly<OrderCreated>();         // Common.Module
    c.AddAssembly<CreateOrder>();          // Orders.Module
    c.AddAssembly<CreateProduct>();        // Products.Module
    c.AddAssembly<GetDashboardReport>();   // Reports.Module
    c.AddAssembly<ClientDispatchHandler>(); // Web (for client dispatch handlers)
});

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

// Map SignalR hub for real-time events
app.MapHub<EventHub>("/hubs/events");

// Map module endpoints - each module exposes its own API endpoints
app.MapOrdersEndpoints();
app.MapProductsEndpoints();
app.MapReportsEndpoints();

// SPA fallback - serves index.html for client-side routing
app.MapFallbackToFile("/index.html");

app.Run();
