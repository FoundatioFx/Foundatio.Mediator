using Foundatio.Mediator;
using Orders.Module.Api;
using Orders.Module.Messages;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Foundatio.Mediator
builder.Services.AddMediator(c => c.AddAssembly<OrderCreated>());

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

// Map Orders module endpoints
app.MapOrdersEndpoints();

// Add a simple health check endpoint
app.MapGet("/", () => "Modular Monolith Sample with Foundatio.Mediator")
    .WithName("Home")
    .WithSummary("Home endpoint");

app.Run();
