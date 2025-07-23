using BenchmarkDotNet.Attributes;
using Foundatio.Mediator;
using Foundatio.Mediator.Benchmarks.Messages;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
public class ThroughputBenchmarks
{
    private IServiceProvider _foundatioServices = null!;
    private IServiceProvider _mediatrServices = null!;
    private IMediator _foundatioMediator = null!;
    private MediatR.IMediator _mediatrMediator = null!;

    private readonly PingCommand[] _pingCommands;
    private readonly GreetingQuery[] _greetingQueries;

    public ThroughputBenchmarks()
    {
        // Pre-create command/query instances to avoid allocation overhead in benchmarks
        _pingCommands = Enumerable.Range(0, 1000)
            .Select(i => new PingCommand($"ping-{i}"))
            .ToArray();

        _greetingQueries = Enumerable.Range(0, 1000)
            .Select(i => new GreetingQuery($"User-{i}"))
            .ToArray();
    }

    [GlobalSetup]
    public void Setup()
    {
        // Setup Foundatio.Mediator
        var foundatioServices = new ServiceCollection();
        Foundatio.Mediator.ServiceCollectionExtensions.AddMediator(foundatioServices);
        _foundatioServices = foundatioServices.BuildServiceProvider();
        _foundatioMediator = _foundatioServices.GetRequiredService<IMediator>();

        // Setup MediatR
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<ThroughputBenchmarks>();
        });
        _mediatrServices = mediatrServices.BuildServiceProvider();
        _mediatrMediator = _mediatrServices.GetRequiredService<MediatR.IMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_foundatioServices as IDisposable)?.Dispose();
        (_mediatrServices as IDisposable)?.Dispose();
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(500)]
    [Arguments(1000)]
    public async Task FoundatioCommandThroughputAsync(int commandCount)
    {
        var tasks = new Task[commandCount];
        for (int i = 0; i < commandCount; i++)
        {
            var command = _pingCommands[i % _pingCommands.Length];
            tasks[i] = _foundatioMediator.InvokeAsync(command).AsTask();
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(500)]
    [Arguments(1000)]
    public async Task MediatRCommandThroughputAsync(int commandCount)
    {
        var tasks = new Task[commandCount];
        for (int i = 0; i < commandCount; i++)
        {
            var command = _pingCommands[i % _pingCommands.Length];
            tasks[i] = _mediatrMediator.Send(command);
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(500)]
    [Arguments(1000)]
    public async Task FoundatioQueryThroughputAsync(int queryCount)
    {
        var tasks = new Task<string>[queryCount];
        for (int i = 0; i < queryCount; i++)
        {
            var query = _greetingQueries[i % _greetingQueries.Length];
            tasks[i] = _foundatioMediator.InvokeAsync<string>(query).AsTask();
        }
        string[] results = await Task.WhenAll(tasks);
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(500)]
    [Arguments(1000)]
    public async Task MediatRQueryThroughputAsync(int queryCount)
    {
        var tasks = new Task<string>[queryCount];
        for (int i = 0; i < queryCount; i++)
        {
            var query = _greetingQueries[i % _greetingQueries.Length];
            tasks[i] = _mediatrMediator.Send(query);
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [Arguments(100)]
    public async Task FoundatioSequentialCommandsAsync(int commandCount)
    {
        for (int i = 0; i < commandCount; i++)
        {
            var command = _pingCommands[i % _pingCommands.Length];
            await _foundatioMediator.InvokeAsync(command);
        }
    }

    [Benchmark]
    [Arguments(100)]
    public async Task MediatRSequentialCommandsAsync(int commandCount)
    {
        for (int i = 0; i < commandCount; i++)
        {
            var command = _pingCommands[i % _pingCommands.Length];
            await _mediatrMediator.Send(command);
        }
    }
}
