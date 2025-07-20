using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Foundatio.Mediator;
using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Handlers.Foundatio;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using MassTransit;

namespace Foundatio.Mediator.Benchmarks;

[Config(typeof(Config))]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class MediatorBenchmarks
{
    private IServiceProvider _foundatioServices = null!;
    private IServiceProvider _mediatrServices = null!;
    private IServiceProvider _masstransitServices = null!;
    private Foundatio.Mediator.IMediator _foundatioMediator = null!;
    private MediatR.IMediator _mediatrMediator = null!;
    private MassTransit.Mediator.IMediator _masstransitMediator = null!;

    // Direct handler instances for baseline comparison
    private readonly FoundatioPingHandler _directPingHandler = new();
    private readonly FoundatioGreetingHandler _directGreetingHandler = new();
    private readonly FoundatioCreateOrderHandler _directCreateOrderHandler = new();
    private readonly FoundatioGetOrderDetailsHandler _directGetOrderDetailsHandler = new();
    private readonly FoundatioUserRegisteredEmailHandler _directEmailHandler = new();
    private readonly FoundatioUserRegisteredAnalyticsHandler _directAnalyticsHandler = new();
    private readonly FoundatioUserRegisteredWelcomeHandler _directWelcomeHandler = new();

    private readonly PingCommand _pingCommand = new("test-123");
    private readonly GreetingQuery _greetingQuery = new("World");
    private readonly CreateOrderCommand _createOrderCommand = new("Product-1", 2, 199.99m, "Customer-1");
    private readonly GetOrderDetailsQuery _getOrderDetailsQuery = new("Order-123");
    private readonly UserRegisteredEvent _userRegisteredEvent = new("User-456", "test@example.com");

    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.Default.WithGcServer(true));
        }
    }

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
            cfg.RegisterServicesFromAssemblyContaining<MediatorBenchmarks>();
        });
        _mediatrServices = mediatrServices.BuildServiceProvider();
        _mediatrMediator = _mediatrServices.GetRequiredService<MediatR.IMediator>();

        // Setup MassTransit
        var masstransitServices = new ServiceCollection();
        masstransitServices.AddMediator(cfg =>
        {
            cfg.AddConsumer<Handlers.MassTransit.MassTransitPingConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitGreetingConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitCreateOrderConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitGetOrderDetailsConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitUserRegisteredEmailConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitUserRegisteredAnalyticsConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitUserRegisteredWelcomeConsumer>();
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

    // Synchronous versions - only include if there are sync handlers
    // [Benchmark]
    // public string FoundatioGreetingQuerySync()
    // {
    //     return _foundatioMediator.Invoke<string>(_greetingQuery);
    // }

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

    [Benchmark]
    public async Task<string> DirectCreateOrderAsync()
    {
        return await _directCreateOrderHandler.HandleAsync(_createOrderCommand);
    }

    [Benchmark]
    public async Task<OrderDetails> DirectGetOrderDetailsAsync()
    {
        return await _directGetOrderDetailsHandler.HandleAsync(_getOrderDetailsQuery);
    }

    [Benchmark]
    public async Task DirectPublishEventAsync()
    {
        // Simulate direct calls to all three handlers (like publish does)
        await Task.WhenAll(
            _directEmailHandler.HandleAsync(_userRegisteredEvent),
            _directAnalyticsHandler.HandleAsync(_userRegisteredEvent),
            _directWelcomeHandler.HandleAsync(_userRegisteredEvent)
        );
    }

    // Complex command benchmarks
    [Benchmark]
    public async Task<string> FoundatioCreateOrderAsync()
    {
        return await _foundatioMediator.InvokeAsync<string>(_createOrderCommand);
    }

    [Benchmark]
    public async Task<string> MediatRCreateOrderAsync()
    {
        return await _mediatrMediator.Send(_createOrderCommand);
    }

    [Benchmark]
    public async Task<string> MassTransitCreateOrderAsync()
    {
        var client = _masstransitMediator.CreateRequestClient<CreateOrderCommand>();
        var response = await client.GetResponse<CreateOrderResponse>(_createOrderCommand);
        return response.Message.OrderId;
    }

    // Complex query benchmarks
    [Benchmark]
    public async Task<OrderDetails> FoundatioGetOrderDetailsAsync()
    {
        return await _foundatioMediator.InvokeAsync<OrderDetails>(_getOrderDetailsQuery);
    }

    [Benchmark]
    public async Task<OrderDetails> MediatRGetOrderDetailsAsync()
    {
        return await _mediatrMediator.Send(_getOrderDetailsQuery);
    }

    [Benchmark]
    public async Task<OrderDetails> MassTransitGetOrderDetailsAsync()
    {
        var client = _masstransitMediator.CreateRequestClient<GetOrderDetailsQuery>();
        var response = await client.GetResponse<OrderDetails>(_getOrderDetailsQuery);
        return response.Message;
    }

    // Publish/notification benchmarks (multiple handlers)
    [Benchmark]
    public async Task FoundatioPublishEventAsync()
    {
        await _foundatioMediator.PublishAsync(_userRegisteredEvent);
    }

    [Benchmark]
    public async Task MediatRPublishEventAsync()
    {
        await _mediatrMediator.Publish(_userRegisteredEvent);
    }

    [Benchmark]
    public async Task MassTransitPublishEventAsync()
    {
        await _masstransitMediator.Publish(_userRegisteredEvent);
    }

    // Cold start benchmarks (first call after setup)
    [Benchmark]
    public async Task FoundatioColdStartAsync()
    {
        var newPingCommand = new PingCommand("cold-start");
        await _foundatioMediator.InvokeAsync(newPingCommand);
    }

    [Benchmark]
    public async Task MediatRColdStartAsync()
    {
        var newPingCommand = new PingCommand("cold-start");
        await _mediatrMediator.Send(newPingCommand);
    }

    [Benchmark]
    public async Task MassTransitColdStartAsync()
    {
        var newPingCommand = new PingCommand("cold-start");
        await _masstransitMediator.Send(newPingCommand);
    }
}
