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
    private readonly FoundatioCreateOrderHandler _directCreateOrderHandler = new();
    private readonly FoundatioFirstOrderCreatedHandler _directOrderCreatedHandler1 = new();
    private readonly FoundatioSecondOrderCreatedHandler _directOrderCreatedHandler2 = new();
    private FoundatioFullQueryHandler _directFullQueryHandler = null!;

    private readonly PingCommand _pingCommand = new("test-123");
    private readonly GetOrder _getOrder = new(42);
    private readonly GetFullQuery _getFullQuery = new(42);
    private readonly GetCachedOrder _getCachedOrder = new(42);
    private readonly UserRegisteredEvent _userRegisteredEvent = new("User-456", "test@example.com");
    private readonly CreateOrder _createOrder = new(123, 99.99m);

    // MediatorNet-specific message types (uses different interfaces)
    private readonly MediatorNetPingCommand _mediatorNetPingCommand = new("test-123");
    private readonly MediatorNetGetOrder _mediatorNetGetOrder = new(42);
    private readonly MediatorNetGetFullQuery _mediatorNetGetFullQuery = new(42);
    private readonly MediatorNetUserRegisteredEvent _mediatorNetUserRegisteredEvent = new("User-456", "test@example.com");
    private readonly MediatorNetCreateOrder _mediatorNetCreateOrder = new(123, 99.99m);
    private readonly MediatorNetGetCachedOrder _mediatorNetGetCachedOrder = new(42);

    [GlobalSetup]
    public void Setup()
    {
        // Setup Foundatio.Mediator
        var foundatioServices = new ServiceCollection();
        foundatioServices.AddSingleton<IOrderService, OrderService>();
        foundatioServices.AddMediator();
        _foundatioServices = foundatioServices.BuildServiceProvider();
        _foundatioMediator = _foundatioServices.GetRequiredService<Foundatio.Mediator.IMediator>();

        // Create direct handler with DI for FullQuery baseline
        _directFullQueryHandler = new FoundatioFullQueryHandler(
            _foundatioServices.GetRequiredService<IOrderService>());

        // Setup MediatR
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddSingleton<IOrderService, OrderService>();
        mediatrServices.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CoreBenchmarks>();
            // Register timing behavior for GetFullQuery to match Foundatio's middleware
            cfg.AddBehavior<MediatR.IPipelineBehavior<GetFullQuery, Order>, Handlers.MediatR.TimingBehavior<GetFullQuery, Order>>();
            // Register short-circuit behavior for GetCachedOrder
            cfg.AddBehavior<MediatR.IPipelineBehavior<GetCachedOrder, Order>, Handlers.MediatR.ShortCircuitBehavior>();
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
            cfg.AddConsumer<Handlers.MassTransit.MassTransitFullQueryConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitCreateOrderConsumer>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitOrderCreatedConsumer1>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitOrderCreatedConsumer2>();
            cfg.AddConsumer<Handlers.MassTransit.MassTransitShortCircuitConsumer>();
        });
        _masstransitServices = masstransitServices.BuildServiceProvider();
        _masstransitMediator = _masstransitServices.GetRequiredService<MassTransit.Mediator.IMediator>();

        // Setup Mediator.SourceGenerator (MediatorNet)
        var mediatorNetServices = new ServiceCollection();
        mediatorNetServices.AddSingleton<IOrderService, OrderService>();
        // Register short-circuit behavior before AddMediator so it's available in the pipeline
        mediatorNetServices.AddSingleton<MediatorLib.IPipelineBehavior<MediatorNetGetCachedOrder, Order>, MediatorNetShortCircuitBehavior>();
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
                    .IncludeType<Handlers.Wolverine.WolverineFullQueryHandler>()
                    .IncludeType<Handlers.Wolverine.WolverineCreateOrderHandler>()
                    .IncludeType<Handlers.Wolverine.WolverineOrderCreatedHandler1>()
                    .IncludeType<Handlers.Wolverine.WolverineOrderCreatedHandler2>()
                    .IncludeType<Handlers.Wolverine.WolverineShortCircuitHandler>();
                // Register short-circuit middleware for GetCachedOrder
                opts.Policies.ForMessagesOfType<GetCachedOrder>().AddMiddleware(typeof(Handlers.Wolverine.WolverineShortCircuitMiddleware));
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
    public async Task Direct_Publish()
    {
        await _directEventHandler.HandleAsync(_userRegisteredEvent);
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

    // Scenario 4: InvokeAsync<T> with DI (Query with dependency injection and middleware)
    [Benchmark]
    public async Task<Order> Direct_FullQuery()
    {
        // Simulate TimingMiddleware.Before
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return await _directFullQueryHandler.HandleAsync(_getFullQuery);
        }
        finally
        {
            // Simulate TimingMiddleware.Finally
            stopwatch.Stop();
        }
    }

    [Benchmark]
    public async Task<Order> Foundatio_FullQuery()
    {
        return await _foundatioMediator.InvokeAsync<Order>(_getFullQuery);
    }

    [Benchmark]
    public async Task<Order> MediatR_FullQuery()
    {
        return await _mediatrMediator.Send(_getFullQuery);
    }

    [Benchmark]
    public async Task<Order> MassTransit_FullQuery()
    {
        var client = _masstransitMediator.CreateRequestClient<GetFullQuery>();
        var response = await client.GetResponse<Order>(_getFullQuery);
        return response.Message;
    }

    [Benchmark]
    public async Task<Order?> Wolverine_FullQuery()
    {
        return await _wolverineBus.InvokeAsync<Order>(_getFullQuery);
    }

    [Benchmark]
    public async Task<Order> MediatorNet_FullQuery()
    {
        return await _mediatorNetMediator.Send(_mediatorNetGetFullQuery);
    }

    // Scenario 5: Cascading messages - invoke returns result and auto-publishes events to multiple handlers
    [Benchmark]
    public async Task<Order> Direct_CascadingMessages()
    {
        var (order, evt) = _directCreateOrderHandler.HandleAsync(_createOrder);
        await _directOrderCreatedHandler1.HandleAsync(evt);
        await _directOrderCreatedHandler2.HandleAsync(evt);
        return order;
    }

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

    // Scenario 6: Short-circuit middleware - returns cached result, handler is never invoked
    // Tests the cost of short-circuiting via middleware (caching, validation, auth, etc.)
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    [Benchmark]
    public Task<Order> Direct_ShortCircuit()
    {
        // Simulate ShortCircuitMiddleware.Before returning cached result
        return Task.FromResult(_cachedOrder);
    }

    [Benchmark]
    public async Task<Order> Foundatio_ShortCircuit()
    {
        return await _foundatioMediator.InvokeAsync<Order>(_getCachedOrder);
    }

    [Benchmark]
    public async Task<Order> MediatR_ShortCircuit()
    {
        return await _mediatrMediator.Send(_getCachedOrder);
    }

    [Benchmark]
    public async Task<Order> MassTransit_ShortCircuit()
    {
        var client = _masstransitMediator.CreateRequestClient<GetCachedOrder>();
        var response = await client.GetResponse<Order>(_getCachedOrder);
        return response.Message;
    }

    [Benchmark]
    public async Task<Order?> Wolverine_ShortCircuit()
    {
        return await _wolverineBus.InvokeAsync<Order>(_getCachedOrder);
    }

    [Benchmark]
    public async Task<Order> MediatorNet_ShortCircuit()
    {
        return await _mediatorNetMediator.Send(_mediatorNetGetCachedOrder);
    }
}
