using BenchmarkDotNet.Attributes;
using Foundatio.Mediator.Benchmarks.Handlers.Foundatio;
using Foundatio.Mediator.Benchmarks.Handlers.ImmediateHandlers;
using Foundatio.Mediator.Benchmarks.Handlers.MediatorNet;
using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;
using MediatorLib = Mediator;

namespace Foundatio.Mediator.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
public class CoreBenchmarks
{
    private IServiceProvider _foundatioServices = null!;
    private IServiceProvider _ihServices = null!;
    private IServiceProvider _mediatrServices = null!;
    private IServiceProvider _masstransitServices = null!;
    private IServiceProvider _mediatorNetServices = null!;

    private Foundatio.Mediator.IMediator _foundatioMediator = null!;

    private ImmediateHandlersCommandHandler.Handler _immediateHandlersCommandHandler = null!;
    private ImmediateHandlersQueryHandler.Handler _immediateHandlersQueryHandler = null!;
    private Publisher<UserRegisteredEvent> _immediateHandlersEventHandler = null!;
    private ImmediateHandlersFullQuery.Handler _immediateHandlersFullQueryHandler = null!;
    private ImmediateHandlersCreateOrderConsumer.Handler _immediateHandlersCreateOrderConsumer = null!;
    private ImmediateHandlersShortCircuitHandler.Handler _immediateHandlersShortCircuitHandler = null!;

    private MediatR.IMediator _mediatrMediator = null!;

    private MassTransit.Mediator.IMediator _masstransitMediator = null!;
    private IRequestClient<GetOrder> _masstransitQueryClient = null!;
    private IRequestClient<GetFullQuery> _masstransitFullQueryClient = null!;
    private IRequestClient<CreateOrder> _masstransitCascadingMessagesClient = null!;
    private IRequestClient<GetCachedOrder> _masstransitShortCircuitClient = null!;

    private MediatorLib.IMediator _mediatorNetMediator = null!;

    private IHost _wolverineHost = null!;
    private IMessageBus _wolverineBus = null!;

    // Direct handler instances for baseline comparison
    private readonly FoundatioCommandHandler _directCommandHandler = new();
    private readonly FoundatioQueryHandler _directQueryHandler = new();
    private readonly FoundatioPublishHandler _directEventHandler = new();
    private readonly FoundatioCascadingMessagesHandler _directCreateOrderHandler = new();
    private FoundatioFullQueryHandler _directFullQueryHandler = null!;

    private readonly PingCommand _pingCommand = new("test-123");
    private readonly GetOrder _getOrder = new(42);
    private readonly GetFullQuery _getFullQuery = new(42);
    private readonly GetCachedOrder _getCachedOrder = new(42);
    private readonly UserRegisteredEvent _userRegisteredEvent = new("User-456", "test@example.com");
    private readonly CreateOrder _createOrder = new(123, 99.99m);

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
        _directFullQueryHandler = new FoundatioFullQueryHandler(_foundatioServices.GetRequiredService<IOrderService>());

        // Setup Immediate Handlers
        var ihServices = new ServiceCollection();
        ihServices.AddFoundatioMediatorBenchmarksBehaviors();
        ihServices.AddFoundatioMediatorBenchmarksHandlers();
        ihServices.AddSingleton<IOrderService, OrderService>();
        ihServices.AddScoped(typeof(Publisher<>));
        _ihServices = ihServices.BuildServiceProvider();
        _immediateHandlersCommandHandler = _ihServices.GetRequiredService<ImmediateHandlersCommandHandler.Handler>();
        _immediateHandlersQueryHandler = _ihServices.GetRequiredService<ImmediateHandlersQueryHandler.Handler>();
        _immediateHandlersEventHandler = _ihServices.GetRequiredService<Publisher<UserRegisteredEvent>>();
        _immediateHandlersFullQueryHandler = _ihServices.GetRequiredService<ImmediateHandlersFullQuery.Handler>();
        _immediateHandlersCreateOrderConsumer = _ihServices.GetRequiredService<ImmediateHandlersCreateOrderConsumer.Handler>();
        _immediateHandlersShortCircuitHandler = _ihServices.GetRequiredService<ImmediateHandlersShortCircuitHandler.Handler>();

        // Setup MediatR
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddSingleton<IOrderService, OrderService>();
        mediatrServices.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<CoreBenchmarks>();
            cfg.AddBehavior<MediatR.IPipelineBehavior<GetFullQuery, Order>, Handlers.MediatR.TimingBehavior<GetFullQuery, Order>>();
            cfg.AddBehavior<MediatR.IPipelineBehavior<GetCachedOrder, Order>, Handlers.MediatR.ShortCircuitBehavior>();
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
            cfg.ConfigureMediator((context, mcfg) =>
            {
                mcfg.UseConsumeFilter(typeof(Handlers.MassTransit.MassTransitTimingFilter<>), context);
                mcfg.UseConsumeFilter(typeof(Handlers.MassTransit.MassTransitShortCircuitFilter<>), context);
            });
        });
        _masstransitServices = masstransitServices.BuildServiceProvider();
        _masstransitMediator = _masstransitServices.GetRequiredService<MassTransit.Mediator.IMediator>();
        _masstransitQueryClient = _masstransitMediator.CreateRequestClient<GetOrder>();
        _masstransitFullQueryClient = _masstransitMediator.CreateRequestClient<GetFullQuery>();
        _masstransitCascadingMessagesClient = _masstransitMediator.CreateRequestClient<CreateOrder>();
        _masstransitShortCircuitClient = _masstransitMediator.CreateRequestClient<GetCachedOrder>();

        // Setup MediatorNet
        var mediatorNetServices = new ServiceCollection();
        mediatorNetServices.AddSingleton<IOrderService, OrderService>();
        mediatorNetServices.AddSingleton<MediatorLib.IPipelineBehavior<GetCachedOrder, Order>, MediatorNetShortCircuitBehavior>();
        mediatorNetServices.AddSingleton<MediatorLib.IPipelineBehavior<GetFullQuery, Order>, MediatorNetTimingBehavior>();
        mediatorNetServices.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Singleton;
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
                // Register timing middleware for GetFullQuery to match Foundatio's middleware
                opts.Policies.ForMessagesOfType<GetFullQuery>().AddMiddleware(typeof(Handlers.Wolverine.WolverineTimingMiddleware));
            })
            .Build();
        _wolverineHost.Start();
        _wolverineBus = _wolverineHost.Services.GetRequiredService<IMessageBus>();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        (_foundatioServices as IDisposable)?.Dispose();
        (_ihServices as IDisposable)?.Dispose();
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
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Command")]
    public ValueTask Direct_Command()
    {
        return _directCommandHandler.HandleAsync(_pingCommand);
    }

    // Scenario 1: InvokeAsync without response (Command)
    [Benchmark]
    [BenchmarkCategory("Command")]
    public ValueTask Foundatio_Command()
    {
        return _foundatioMediator.InvokeAsync(_pingCommand);
    }

    [Benchmark]
    [BenchmarkCategory("Command")]
    public ValueTask<ValueTuple> ImmediateHandlers_Command()
    {
        return _immediateHandlersCommandHandler.HandleAsync(_pingCommand);
    }

    [Benchmark]
    [BenchmarkCategory("Command")]
    public Task MediatR_Command()
    {
        return _mediatrMediator.Send(_pingCommand);
    }

    [Benchmark]
    [BenchmarkCategory("Command")]
    public Task MassTransit_Command()
    {
        return _masstransitMediator.Send(_pingCommand);
    }

    [Benchmark]
    [BenchmarkCategory("Command")]
    public Task Wolverine_Command()
    {
        return _wolverineBus.InvokeAsync(_pingCommand);
    }

    [Benchmark]
    [BenchmarkCategory("Command")]
    public ValueTask<global::Mediator.Unit> MediatorNet_Command()
    {
        return _mediatorNetMediator.Send(_pingCommand);
    }

    // Scenario 2: InvokeAsync<T> (Query)
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Query")]
    public ValueTask<Order> Direct_Query()
    {
        return _directQueryHandler.HandleAsync(_getOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Query")]
    public ValueTask<Order> Foundatio_Query()
    {
        return _foundatioMediator.InvokeAsync<Order>(_getOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Query")]
    public ValueTask<Order> ImmediateHandlers_Query()
    {
        return _immediateHandlersQueryHandler.HandleAsync(_getOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Query")]
    public Task<Order> MediatR_Query()
    {
        return _mediatrMediator.Send(_getOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Query")]
    public async ValueTask<Order> MassTransit_Query()
    {
        var response = await _masstransitQueryClient.GetResponse<Order>(_getOrder);
        return response.Message;
    }

    [Benchmark]
    [BenchmarkCategory("Query")]
    public Task<Order?> Wolverine_Query()
    {
        return _wolverineBus.InvokeAsync<Order?>(_getOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Query")]
    public ValueTask<Order> MediatorNet_Query()
    {
        return _mediatorNetMediator.Send(_getOrder);
    }

    // Scenario 3: PublishAsync with a single handler
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Publish")]
    public ValueTask Direct_Publish()
    {
        return _directEventHandler.HandleAsync(_userRegisteredEvent);
    }

    [Benchmark]
    [BenchmarkCategory("Publish")]
    public ValueTask Foundatio_Publish()
    {
        return _foundatioMediator.PublishAsync(_userRegisteredEvent);
    }

    [Benchmark]
    [BenchmarkCategory("Publish")]
    public ValueTask ImmediateHandlers_Publish()
    {
        return _immediateHandlersEventHandler.Publish(_userRegisteredEvent);
    }

    [Benchmark]
    [BenchmarkCategory("Publish")]
    public Task MediatR_Publish()
    {
        return _mediatrMediator.Publish(_userRegisteredEvent);
    }

    [Benchmark]
    [BenchmarkCategory("Publish")]
    public Task MassTransit_Publish()
    {
        return _masstransitMediator.Publish(_userRegisteredEvent);
    }

    [Benchmark]
    [BenchmarkCategory("Publish")]
    public ValueTask Wolverine_Publish()
    {
        return _wolverineBus.PublishAsync(_userRegisteredEvent);
    }

    [Benchmark]
    [BenchmarkCategory("Publish")]
    public ValueTask MediatorNet_Publish()
    {
        return _mediatorNetMediator.Publish(_userRegisteredEvent);
    }

    // Scenario 4: InvokeAsync<T> with DI (Query with dependency injection and middleware)
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Full Query")]
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
    [BenchmarkCategory("Full Query")]
    public ValueTask<Order> Foundatio_FullQuery()
    {
        return _foundatioMediator.InvokeAsync<Order>(_getFullQuery);
    }

    [Benchmark]
    [BenchmarkCategory("Full Query")]
    public ValueTask<Order> ImmediateHandlers_FullQuery()
    {
        return _immediateHandlersFullQueryHandler.HandleAsync(_getFullQuery);
    }

    [Benchmark]
    [BenchmarkCategory("Full Query")]
    public Task<Order> MediatR_QueryWithDependencies()
    {
        return _mediatrMediator.Send(_getFullQuery);
    }

    [Benchmark]
    [BenchmarkCategory("Full Query")]
    public async ValueTask<Order> MassTransit_FullQuery()
    {
        var response = await _masstransitFullQueryClient.GetResponse<Order>(_getFullQuery);
        return response.Message;
    }

    [Benchmark]
    [BenchmarkCategory("Full Query")]
    public Task<Order> Wolverine_FullQuery()
    {
        return _wolverineBus.InvokeAsync<Order>(_getFullQuery);
    }

    [Benchmark]
    [BenchmarkCategory("Full Query")]
    public ValueTask<Order> MediatorNet_FullQuery()
    {
        return _mediatorNetMediator.Send(_getFullQuery);
    }

    // Scenario 5: Cascading messages - invoke returns result and auto-publishes events to multiple handlers
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Cascading")]
    public async Task<Order> Direct_CascadingMessages()
    {
        var (order, evt) = await _directCreateOrderHandler.HandleAsync(_createOrder);
        await FoundatioFirstOrderCreatedHandler.HandleAsync(evt);
        await FoundatioSecondOrderCreatedHandler.HandleAsync(evt);
        return order;
    }

    [Benchmark]
    [BenchmarkCategory("Cascading")]
    public ValueTask<Order> Foundatio_CascadingMessages()
    {
        return _foundatioMediator.InvokeAsync<Order>(_createOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Cascading")]
    public ValueTask<Order> ImmediateHandlers_CascadingMessages()
    {
        return _immediateHandlersCreateOrderConsumer.HandleAsync(_createOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Cascading")]
    public Task<Order> MediatR_CascadingMessages()
    {
        return _mediatrMediator.Send(_createOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Cascading")]
    public async ValueTask<Order> MassTransit_CascadingMessages()
    {
        var response = await _masstransitCascadingMessagesClient.GetResponse<Order>(_createOrder);
        return response.Message;
    }

    [Benchmark]
    [BenchmarkCategory("Cascading")]
    public Task<Order> Wolverine_CascadingMessages()
    {
        return _wolverineBus.InvokeAsync<Order>(_createOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Cascading")]
    public ValueTask<Order> MediatorNet_CascadingMessages()
    {
        return _mediatorNetMediator.Send(_createOrder);
    }

    // Scenario 6: Short-circuit middleware - returns cached result, handler is never invoked
    // Tests the cost of short-circuiting via middleware (caching, validation, auth, etc.)
    private static readonly Order _cachedOrder = new(999, 49.99m, DateTime.UtcNow);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Short Circuit")]
    public ValueTask<Order> Direct_ShortCircuit()
    {
        // Simulate ShortCircuitMiddleware.Before returning cached result
        // awaiting created `ValueTask<>` to remove async state machine as variance between
        // this test and others
        return ValueTask.FromResult(_cachedOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Short Circuit")]
    public ValueTask<Order> Foundatio_ShortCircuit()
    {
        return _foundatioMediator.InvokeAsync<Order>(_getCachedOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Short Circuit")]
    public ValueTask<Order> ImmediateHandlers_ShortCircuit()
    {
        return _immediateHandlersShortCircuitHandler.HandleAsync(_getCachedOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Short Circuit")]
    public Task<Order> MediatR_ShortCircuit()
    {
        return _mediatrMediator.Send(_getCachedOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Short Circuit")]
    public async ValueTask<Order> MassTransit_ShortCircuit()
    {
        var response = await _masstransitShortCircuitClient.GetResponse<Order>(_getCachedOrder);
        return response.Message;
    }

    [Benchmark]
    [BenchmarkCategory("Short Circuit")]
    public Task<Order> Wolverine_ShortCircuit()
    {
        return _wolverineBus.InvokeAsync<Order>(_getCachedOrder);
    }

    [Benchmark]
    [BenchmarkCategory("Short Circuit")]
    public ValueTask<Order> MediatorNet_ShortCircuit()
    {
        return _mediatorNetMediator.Send(_getCachedOrder);
    }
}
