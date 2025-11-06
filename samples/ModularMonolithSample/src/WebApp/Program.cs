using Common.Module;
using Foundatio.Mediator;
using Orders.Module;
using Orders.Module.Api;
using Orders.Module.Messages;
using Products.Module;
using Products.Module.Api;
using Products.Module.Messages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Foundatio.Mediator with assemblies from all modules
builder.Services.AddMediator(c =>
{
    c.AddAssembly<OrderCreated>();   // Orders.Module
    c.AddAssembly<ProductCreated>(); // Products.Module
});

// Add module services
builder.Services.AddCommonModule();
builder.Services.AddOrdersModule();
builder.Services.AddProductsModule();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

// Map module endpoints
app.MapOrdersEndpoints();
app.MapProductEndpoints();

// Add a simple health check endpoint
app.MapGet("/", () => "Modular Monolith Sample with Foundatio.Mediator")
    .WithName("Home")
    .WithSummary("Home endpoint");

app.Run();
