using ConsoleSample;
using Foundatio.Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

if (args.Any(a => string.Equals(a, "--web", StringComparison.OrdinalIgnoreCase)))
{
	await RunMinimalApiAsync(args);
	return;
}

// Create application host
var builder = Host.CreateApplicationBuilder(args);

// Configure all services
builder.Services.ConfigureServices();

var host = builder.Build();

// Get mediator and run samples
var mediator = host.Services.GetRequiredService<IMediator>();
var sampleRunner = new SampleRunner(mediator, host.Services);

await sampleRunner.RunAllSamplesAsync();

static async Task RunMinimalApiAsync(string[] args)
{
	var builder = WebApplication.CreateBuilder(args);
	builder.Services.ConfigureServices();
	builder.Services.AddEndpointsApiExplorer();
	builder.Services.AddOpenApi();

	var app = builder.Build();
	app.MapOpenApi();
	app.MapScalarApiReference(options =>
	{
		options.Title = "Foundatio.Mediator Console Sample";
	});
	app.MapMediatorEndpoints();

	await app.RunAsync();
}
