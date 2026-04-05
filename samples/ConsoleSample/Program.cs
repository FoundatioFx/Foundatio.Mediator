using ConsoleSample;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Create application host
var builder = Host.CreateApplicationBuilder(args);

// Configure all services
builder.Services.ConfigureServices();

var host = builder.Build();

// Get mediator and run samples
var mediator = host.Services.GetRequiredService<IMediator>();
var sampleRunner = new SampleRunner(mediator, host.Services);

await sampleRunner.RunAllSamplesAsync();
