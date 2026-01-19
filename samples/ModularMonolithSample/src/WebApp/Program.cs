using Common.Module;
using Foundatio.Mediator;
using Orders.Module;
using Orders.Module.Messages;
using Products.Module;
using Products.Module.Messages;
using Scalar.AspNetCore;
using WebApp.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Add Foundatio.Mediator with assemblies from all modules
builder.Services.AddMediator(c =>
{
    c.SetMediatorLifetime(ServiceLifetime.Scoped);
    c.AddAssembly<OrderCreated>();   // Orders.Module
    c.AddAssembly<ProductCreated>(); // Products.Module
});

// Add module services
builder.Services.AddCommonModule();
builder.Services.AddOrdersModule();
builder.Services.AddProductsModule();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

// Map module endpoints
app.MapOrdersEndpoints();
app.MapProductsEndpoints();

// Add a simple health check endpoint
app.MapGet("/", () => "Modular Monolith Sample with Foundatio.Mediator")
    .WithName("Home")
    .WithSummary("Home endpoint");

// Map WebApp endpoints (these call handlers in other modules via cross-assembly interception)
app.MapDashboardEndpoints();
app.MapSearchEndpoints();

app.Run();
