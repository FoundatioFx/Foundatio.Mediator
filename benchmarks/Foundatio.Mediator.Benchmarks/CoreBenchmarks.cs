using BenchmarkDotNet.Attributes;
using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Handlers.Foundatio;
using Foundatio.Mediator.Benchmarks.Handlers.MediatorNet;
using Foundatio.Mediator.Benchmarks.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MassTransit;
using Wolverine;
using MediatorLib = Mediator;

namespace Foundatio.Mediator.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class CoreBenchmarks
{
    private IServiceProvider _foundatioServices = null!;
    private IServiceProvider _mediatrServices = null!;
    private IServiceProvider _masstransitServices = null!;
    private IServiceProvider _mediatorNetServices = null!;
    private IHost _wolverineHost = null!;
    private Foundatio.Mediator.IMediator _foundatioMediator = null!;
    private MediatR.IMediator _mediatrMediator = null!;
    private MassTransit.Mediator.IMediator _masstransitMediator = null!;
    private MediatorLib.IMediator _mediatorNetMediator = null!;
    private IMessageBus _wolverineBus = null!;

    // Direct handler instances for baseline comparison
    private readonly FoundatioCommandHandler _directCommandHandler = new();
    private readonly FoundatioQueryHandler _directQueryHandler = new();
    private readonly FoundatioEventHandler _directEventHandler = new();
    private FoundatioQueryWithDependenciesHandler _directQueryWithDependenciesHandler = null!;

    private readonly PingCommand _pingCommand = new("test-123");
    private readonly GetOrder _getOrder = new(42);
    private readonly GetOrderWithDependencies _getOrderWithDependencies = new(42);
    private readonly GetOrderShortCircuit _getOrderShortCircuit = new(42);
    private readonly UserRegisteredEvent _userRegisteredEvent = new("User-456", "test@example.com");
    private readonly CreateOrder _createOrder = new(123, 99.99m);

    // MediatorNet-specific message types (uses different interfaces)
    private readonly MediatorNetPingCommand _mediatorNetPingCommand = new("test-123");
    private readonly MediatorNetGetOrder _mediatorNetGetOrder = new(42);
    private readonly MediatorNetGetOrderWithDependencies _mediatorNetGetOrderWithDependencies = new(42);
    private readonly MediatorNetUserRegisteredEvent _mediatorNetUserRegisteredEvent = new("User-456", "test@example.com");
    private readonly MediatorNetCreateOrder _mediatorNetCreateOrder = new(123, 99.99m);

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
            // Register timing behavior for GetOrderWithDependencies to match Foundatio's middleware
            cfg.AddBehavior<MediatR.IPipelineBehavior<GetOrderWithDependencies, Order>, Handlers.MediatR.TimingBehavior<GetOrderWithDependencies, Order>>();
            // Use parallel notification publisher to match Foundatio/MassTransit behavior
            cfg.NotificationPublisher = new MediatR.NotificationPublishers.TaskWhenAllPublisher();
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
            cfg.AddConsumer<Handlers.MassTransit.MassTransitEventConsumer2>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitQueryWithDependenciesConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitCreateOrderConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitOrderCreatedConsumer1>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitOrderCreatedConsumer2>();
        });
        _masstransitServices = masstransitServices.BuildServiceProvider();
        _masstransitMediator = _masstransitServices.GetRequiredService<MassTransit.Mediator.IMediator>();

        // Setup Mediator.SourceGenerator (MediatorNet)
        var mediatorNetServices = new ServiceCollection();
        mediatorNetServices.AddSingleton<IOrderService, OrderService>();
        mediatorNetServices.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Singleton;
            // Use parallel notification publisher to match Foundatio/MassTransit behavior
            options.NotificationPublisherType = typeof(MediatorLib.TaskWhenAllPublisher);
        });
        _mediatorNetServices = mediatorNetServices.BuildServiceProvider();
        _mediatorNetMediator = _mediatorNetServices.GetRequiredService<MediatorLib.IMediator>();

        // Setup Wolverine
        _wolverineHost = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(opts =>
            {
                opts.Services.AddSingleton<IOrderService, OrderService>();
                // Only include Wolverine-specific handlers (disable auto-discovery to avoid other libraries' handlers)
                opts.Discovery.DisableConventionalDiscovery()
                    .IncludeType<Handlers.Wolverine.WolverineCommandHandler>()
                    .IncludeType<Handlers.Wolverine.WolverineQueryHandler>()
                    .IncludeType<Handlers.Wolverine.WolverineEventHandler>()
                    .IncludeType<Handlers.Wolverine.WolverineEventHandler2>()
                    .IncludeType<Handlers.Wolverine.WolverineQueryWithDependenciesHandler>()
                    .IncludeType<Handlers.Wolverine.WolverineCreateOrderHandler>()
                    .IncludeType<Handlers.Wolverine.WolverineOrderCreatedHandler1>()
                    .IncludeType<Handlers.Wolverine.WolverineOrderCreatedHandler2>();
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

        (_mediatorNetServices as IDisposable)?.Dispose();

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

    [Benchmark]
    public async Task MediatorNet_Command()
    {
        await _mediatorNetMediator.Send(_mediatorNetPingCommand);
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

    [Benchmark]
    public async Task<Order> MediatorNet_Query()
    {
        return await _mediatorNetMediator.Send(_mediatorNetGetOrder);
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

    [Benchmark]
    public async Task MediatorNet_Publish()
    {
        await _mediatorNetMediator.Publish(_mediatorNetUserRegisteredEvent);
    }

    // Scenario 4: InvokeAsync<T> with DI (Query with dependency injection)
    [Benchmark]
    public async Task<Order> Foundatio_FullQuery()
    {
        return await _foundatioMediator.InvokeAsync<Order>(_getOrderWithDependencies);
    }

    [Benchmark]
    public async Task<Order> MediatR_FullQuery()
    {
        return await _mediatrMediator.Send(_getOrderWithDependencies);
    }

    [Benchmark]
    public async Task<Order> MassTransit_FullQuery()
    {
        var client = _masstransitMediator.CreateRequestClient<GetOrderWithDependencies>();
        var response = await client.GetResponse<Order>(_getOrderWithDependencies);
        return response.Message;
    }

    [Benchmark]
    public async Task<Order?> Wolverine_FullQuery()
    {
        return await _wolverineBus.InvokeAsync<Order>(_getOrderWithDependencies);
    }

    [Benchmark]
    public async Task<Order> MediatorNet_FullQuery()
    {
        return await _mediatorNetMediator.Send(_mediatorNetGetOrderWithDependencies);
    }

    // Scenario 5: Cascading messages - invoke returns result and auto-publishes events to multiple handlers
    [Benchmark]
    public async Task<Order> Foundatio_CascadingMessages()
    {
        return await _foundatioMediator.InvokeAsync<Order>(_createOrder);
    }

    [Benchmark]
    public async Task<Order> MediatR_CascadingMessages()
    {
        return await _mediatrMediator.Send(_createOrder);
    }

    [Benchmark]
    public async Task<Order> MassTransit_CascadingMessages()
    {
        var client = _masstransitMediator.CreateRequestClient<CreateOrder>();
        var response = await client.GetResponse<Order>(_createOrder);
        return response.Message;
    }

    [Benchmark]
    public async Task<Order?> Wolverine_CascadingMessages()
    {
        return await _wolverineBus.InvokeAsync<Order>(_createOrder);
    }

    [Benchmark]
    public async Task<Order> MediatorNet_CascadingMessages()
    {
        return await _mediatorNetMediator.Send(_mediatorNetCreateOrder);
    }

    // Scenario 6: Short-circuit middleware - Foundatio-only feature
    // Middleware returns cached result, handler is never invoked
    [Benchmark]
    public async Task<Order> Foundatio_ShortCircuit()
    {
        return await _foundatioMediator.InvokeAsync<Order>(_getOrderShortCircuit);
    }
}
