using BenchmarkDotNet.Attributes;
using Foundatio.Mediator.Benchmarks.Messages;
using Foundatio.Mediator.Benchmarks.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Benchmarks;

/// <summary>
/// Foundatio-only benchmarks for measuring library performance improvements.
/// Use this mode when iterating on library performance optimizations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class FoundatioBenchmarks
{
    private IServiceProvider _foundatioServices = null!;
    private Foundatio.Mediator.IMediator _foundatioMediator = null!;

    private readonly PingCommand _pingCommand = new("test-123");
    private readonly GetOrder _getOrder = new(42);
    private readonly GetFullQuery _getFullQuery = new(42);
    private readonly GetCachedOrder _getCachedOrder = new(42);
    private readonly UserRegisteredEvent _userRegisteredEvent = new("User-456", "test@example.com");
    private readonly CreateOrder _createOrder = new(123, 99.99m);

    [GlobalSetup]
    public void Setup()
    {
        var foundatioServices = new ServiceCollection();
        foundatioServices.AddSingleton<IOrderService, OrderService>();
        foundatioServices.AddMediator();
        _foundatioServices = foundatioServices.BuildServiceProvider();
        _foundatioMediator = _foundatioServices.GetRequiredService<Foundatio.Mediator.IMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_foundatioServices as IDisposable)?.Dispose();
    }

    [Benchmark]
    public ValueTask Command()
    {
        return _foundatioMediator.InvokeAsync(_pingCommand);
    }

    [Benchmark]
    public ValueTask<Order> Query()
    {
        return _foundatioMediator.InvokeAsync<Order>(_getOrder);
    }

    [Benchmark]
    public ValueTask Publish()
    {
        return _foundatioMediator.PublishAsync(_userRegisteredEvent);
    }

    [Benchmark]
    public ValueTask<Order> FullQuery()
    {
        return _foundatioMediator.InvokeAsync<Order>(_getFullQuery);
    }

    [Benchmark]
    public ValueTask<Order> CascadingMessages()
    {
        return _foundatioMediator.InvokeAsync<Order>(_createOrder);
    }

    [Benchmark]
    public ValueTask<Order> ShortCircuit()
    {
        return _foundatioMediator.InvokeAsync<Order>(_getCachedOrder);
    }
}
