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
    private readonly GetOrderWithDependencies _getOrderWithDependencies = new(42);
    private readonly UserRegisteredEvent _userRegisteredEvent = new("User-456", "test@example.com");

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
    public async Task Command()
    {
        await _foundatioMediator.InvokeAsync(_pingCommand);
    }

    [Benchmark]
    public async Task<Order> Query()
    {
        return await _foundatioMediator.InvokeAsync<Order>(_getOrder);
    }

    [Benchmark]
    public async Task Publish()
    {
        await _foundatioMediator.PublishAsync(_userRegisteredEvent);
    }

    [Benchmark]
    public async Task<Order> QueryWithDependencies()
    {
        return await _foundatioMediator.InvokeAsync<Order>(_getOrderWithDependencies);
    }
}
