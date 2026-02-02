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

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Add Foundatio.Mediator with assemblies from all modules
builder.Services.AddMediator(c =>
{
    // Register the mediator as scoped to ensure correct DI scope resolution
    c.SetMediatorLifetime(ServiceLifetime.Scoped);
    c.AddAssembly<OrderCreated>();       // Common.Module
    c.AddAssembly<CreateOrder>();        // Orders.Module
    c.AddAssembly<CreateProduct>();      // Products.Module
    c.AddAssembly<GetDashboardReport>(); // Reports.Module
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

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

// Map module endpoints - each module exposes its own API endpoints
app.MapOrdersEndpoints();
app.MapProductsEndpoints();
app.MapReportsEndpoints();

// Add a simple health check endpoint
app.MapGet("/", () => "Modular Monolith Sample with Foundatio.Mediator")
    .WithName("Home")
    .WithSummary("Home endpoint");

app.Run();
