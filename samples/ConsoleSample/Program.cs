using ConsoleSample;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Create application host
var builder = Host.CreateApplicationBuilder(args);

// Check if --sqs flag is passed
var useSqs = args.Contains("--sqs", StringComparer.OrdinalIgnoreCase);

// Configure all services
builder.Services.ConfigureServices(useSqs);

var host = builder.Build();

// Start the host so background services (queue workers) run
await host.StartAsync();

// Get mediator and run samples
var mediator = host.Services.GetRequiredService<IMediator>();
var sampleRunner = new SampleRunner(mediator);

await sampleRunner.RunAllSamplesAsync();

// Stop the host gracefully
await host.StopAsync();
