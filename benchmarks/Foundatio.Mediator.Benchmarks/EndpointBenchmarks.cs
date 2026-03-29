using BenchmarkDotNet.Attributes;
using Foundatio.Mediator.Benchmarks.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Benchmarks;

/// <summary>
/// Compares two endpoint approaches:
///   1. Manual — hand-coded minimal API endpoint
///   2. Mediator — Foundatio.Mediator generated endpoint
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class EndpointBenchmarks
{
    private WebApplication _app = null!;
    private HttpClient _manualClient = null!;
    private HttpClient _mediatorClient = null!;

    [GlobalSetup]
    public void Setup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IOrderService, OrderService>();
        builder.Services.AddMediator();

        _app = builder.Build();

        // Manual endpoint — same work as the generated handler
        _app.MapGet("/manual/items/{itemId}", (string itemId) =>
            Results.Ok(new Order(42, 99.99m, DateTime.UtcNow)));

        _app.MapMediatorEndpoints();
        _app.StartAsync().GetAwaiter().GetResult();

        _manualClient = _app.GetTestClient();
        _mediatorClient = _app.GetTestClient();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _manualClient.Dispose();
        _mediatorClient.Dispose();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    public Task Manual()
        => _manualClient.GetAsync("/manual/items/x-1");

    [Benchmark]
    public Task MediatorEndpoint()
        => _mediatorClient.GetAsync("/api/bench-items/x-1");
}
