using BenchmarkDotNet.Attributes;
using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Handlers.Foundatio;
using Foundatio.Mediator.Benchmarks.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MassTransit;
using Wolverine;

namespace Foundatio.Mediator.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class CoreBenchmarks
{
    private IServiceProvider _foundatioServices = null!;
    private IServiceProvider _mediatrServices = null!;
    private IServiceProvider _masstransitServices = null!;
    private IHost _wolverineHost = null!;
    private Foundatio.Mediator.IMediator _foundatioMediator = null!;
    private MediatR.IMediator _mediatrMediator = null!;
    private MassTransit.Mediator.IMediator _masstransitMediator = null!;
    private IMessageBus _wolverineBus = null!;

    // Direct handler instances for baseline comparison
    private readonly FoundatioCommandHandler _directCommandHandler = new();
    private readonly FoundatioQueryHandler _directQueryHandler = new();
    private readonly FoundatioEventHandler _directEventHandler = new();
    private FoundatioQueryWithDependenciesHandler _directQueryWithDependenciesHandler = null!;

    private readonly PingCommand _pingCommand = new("test-123");
    private readonly GetOrder _getOrder = new(42);
    private readonly GetOrderWithDependencies _getOrderWithDependencies = new(42);
    private readonly UserRegisteredEvent _userRegisteredEvent = new("User-456", "test@example.com");

    [GlobalSetup]
    public void Setup()
    {
        // Setup Foundatio.Mediator
        var foundatioServices = new ServiceCollection();
        foundatioServices.AddSingleton<IOrderService, OrderService>();
        foundatioServices.AddMediator();
        _foundatioServices = foundatioServices.BuildServiceProvider();
        _foundatioMediator = _foundatioServices.GetRequiredService<Foundatio.Mediator.IMediator>();

        // Create direct handler with DI
        _directQueryWithDependenciesHandler = new FoundatioQueryWithDependenciesHandler(
            _foundatioServices.GetRequiredService<IOrderService>());

        // Setup MediatR
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddSingleton<IOrderService, OrderService>();
        mediatrServices.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CoreBenchmarks>();
        });
        _mediatrServices = mediatrServices.BuildServiceProvider();
        _mediatrMediator = _mediatrServices.GetRequiredService<MediatR.IMediator>();

        // Setup MassTransit
        var masstransitServices = new ServiceCollection();
        masstransitServices.AddSingleton<IOrderService, OrderService>();
        masstransitServices.AddMediator(cfg =>
        {
            cfg.AddConsumer<Handlers.MassTransit.MassTransitCommandConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitQueryConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitEventConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitQueryWithDependenciesConsumer>();
        });
        _masstransitServices = masstransitServices.BuildServiceProvider();
        _masstransitMediator = _masstransitServices.GetRequiredService<MassTransit.Mediator.IMediator>();

        // Setup Wolverine
        _wolverineHost = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(opts =>
            {
                opts.Services.AddSingleton<IOrderService, OrderService>();
                opts.Discovery.IncludeAssembly(typeof(CoreBenchmarks).Assembly);
            })
            .Build();
        _wolverineHost.Start();
        _wolverineBus = _wolverineHost.Services.GetRequiredService<IMessageBus>();
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

        await _wolverineHost.StopAsync();
        _wolverineHost.Dispose();
    }

    // Baseline: Direct method calls (no mediator overhead)
    [Benchmark]
    public async Task Direct_Command()
    {
        await _directCommandHandler.HandleAsync(_pingCommand);
    }

    [Benchmark]
    public async Task<Order> Direct_Query()
    {
        return await _directQueryHandler.HandleAsync(_getOrder);
    }

    [Benchmark]
    public async Task Direct_Event()
    {
        await _directEventHandler.HandleAsync(_userRegisteredEvent);
    }

    [Benchmark]
    public async Task<Order> Direct_QueryWithDependencies()
    {
        return await _directQueryWithDependenciesHandler.HandleAsync(_getOrderWithDependencies);
    }

    // Scenario 1: InvokeAsync without response (Command)
    [Benchmark]
    public async Task Foundatio_Command()
    {
        await _foundatioMediator.InvokeAsync(_pingCommand);
    }

    [Benchmark]
    public async Task MediatR_Command()
    {
        await _mediatrMediator.Send(_pingCommand);
    }

    [Benchmark]
    public async Task MassTransit_Command()
    {
        await _masstransitMediator.Send(_pingCommand);
    }

    [Benchmark]
    public async Task Wolverine_Command()
    {
        await _wolverineBus.InvokeAsync(_pingCommand);
    }

    // Scenario 2: InvokeAsync<T> (Query)
    [Benchmark]
    public async Task<Order> Foundatio_Query()
    {
        return await _foundatioMediator.InvokeAsync<Order>(_getOrder);
    }

    [Benchmark]
    public async Task<Order> MediatR_Query()
    {
        return await _mediatrMediator.Send(_getOrder);
    }

    [Benchmark]
    public async Task<Order> MassTransit_Query()
    {
        var client = _masstransitMediator.CreateRequestClient<GetOrder>();
        var response = await client.GetResponse<Order>(_getOrder);
        return response.Message;
    }

    [Benchmark]
    public async Task<Order?> Wolverine_Query()
    {
        return await _wolverineBus.InvokeAsync<Order>(_getOrder);
    }

    // Scenario 3: PublishAsync with a single handler
    [Benchmark]
    public async Task Foundatio_Publish()
    {
        await _foundatioMediator.PublishAsync(_userRegisteredEvent);
    }

    [Benchmark]
    public async Task MediatR_Publish()
    {
        await _mediatrMediator.Publish(_userRegisteredEvent);
    }

    [Benchmark]
    public async Task MassTransit_Publish()
    {
        await _masstransitMediator.Publish(_userRegisteredEvent);
    }

    [Benchmark]
    public async Task Wolverine_Publish()
    {
        await _wolverineBus.PublishAsync(_userRegisteredEvent);
    }

    // Scenario 4: InvokeAsync<T> with DI (Query with dependency injection)
    [Benchmark]
    public async Task<Order> Foundatio_QueryWithDependencies()
    {
        return await _foundatioMediator.InvokeAsync<Order>(_getOrderWithDependencies);
    }

    [Benchmark]
    public async Task<Order> MediatR_QueryWithDependencies()
    {
        return await _mediatrMediator.Send(_getOrderWithDependencies);
    }

    [Benchmark]
    public async Task<Order> MassTransit_QueryWithDependencies()
    {
        var client = _masstransitMediator.CreateRequestClient<GetOrderWithDependencies>();
        var response = await client.GetResponse<Order>(_getOrderWithDependencies);
        return response.Message;
    }

    [Benchmark]
    public async Task<Order?> Wolverine_QueryWithDependencies()
    {
        return await _wolverineBus.InvokeAsync<Order>(_getOrderWithDependencies);
    }
}
