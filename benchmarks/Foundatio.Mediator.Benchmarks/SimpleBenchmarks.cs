using BenchmarkDotNet.Attributes;
using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Handlers.Foundatio;
using Microsoft.Extensions.DependencyInjection;
using MassTransit;

namespace Foundatio.Mediator.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
public class SimpleBenchmarks
{
    private IServiceProvider _foundatioServices = null!;
    private IServiceProvider _mediatrServices = null!;
    private IServiceProvider _masstransitServices = null!;
    private Foundatio.Mediator.IMediator _foundatioMediator = null!;
    private MediatR.IMediator _mediatrMediator = null!;
    private MassTransit.Mediator.IMediator _masstransitMediator = null!;

    private readonly PingCommand _pingCommand = new("test-123");
    private readonly GreetingQuery _greetingQuery = new("World");

    // Direct handler instances for baseline comparison
    private readonly FoundatioPingHandler _directPingHandler = new();
    private readonly FoundatioGreetingHandler _directGreetingHandler = new();

    [GlobalSetup]
    public void Setup()
    {
        // Setup Foundatio.Mediator
        var foundatioServices = new ServiceCollection();
        foundatioServices.AddMediator();
        _foundatioServices = foundatioServices.BuildServiceProvider();
        _foundatioMediator = _foundatioServices.GetRequiredService<Foundatio.Mediator.IMediator>();

        // Setup MediatR
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<SimpleBenchmarks>();
        });
        _mediatrServices = mediatrServices.BuildServiceProvider();
        _mediatrMediator = _mediatrServices.GetRequiredService<MediatR.IMediator>();

        // Setup MassTransit
        var masstransitServices = new ServiceCollection();
        masstransitServices.AddMediator(cfg =>
        {
            cfg.AddConsumer<Handlers.MassTransit.MassTransitPingConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitGreetingConsumer>();
        });
        _masstransitServices = masstransitServices.BuildServiceProvider();
        _masstransitMediator = _masstransitServices.GetRequiredService<MassTransit.Mediator.IMediator>();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        (_foundatioServices as IDisposable)?.Dispose();
        (_mediatrServices as IDisposable)?.Dispose();

        if (_masstransitServices is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else
            (_masstransitServices as IDisposable)?.Dispose();
    }

    // Simple command benchmarks (no return value)
    [Benchmark]
    public async Task FoundatioPingCommandAsync()
    {
        await _foundatioMediator.InvokeAsync(_pingCommand);
    }

    [Benchmark]
    public async Task MediatRPingCommandAsync()
    {
        await _mediatrMediator.Send(_pingCommand);
    }

    [Benchmark]
    public async Task MassTransitPingCommandAsync()
    {
        await _masstransitMediator.Send(_pingCommand);
    }

    // Query benchmarks (with return value)
    [Benchmark]
    public async Task<string> FoundatioGreetingQueryAsync()
    {
        return await _foundatioMediator.InvokeAsync<string>(_greetingQuery);
    }

    [Benchmark]
    public async Task<string> MediatRGreetingQueryAsync()
    {
        return await _mediatrMediator.Send(_greetingQuery);
    }

    [Benchmark]
    public async Task<string> MassTransitGreetingQueryAsync()
    {
        var client = _masstransitMediator.CreateRequestClient<GreetingQuery>();
        var response = await client.GetResponse<GreetingResponse>(_greetingQuery);
        return response.Message.Message;
    }

    // Direct method call benchmarks (baseline for comparison)
    [Benchmark]
    public async Task DirectPingCommandAsync()
    {
        await _directPingHandler.HandleAsync(_pingCommand);
    }

    [Benchmark]
    public async Task<string> DirectGreetingQueryAsync()
    {
        return await _directGreetingHandler.HandleAsync(_greetingQuery);
    }
}
