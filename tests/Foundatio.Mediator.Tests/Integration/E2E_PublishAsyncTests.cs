using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_PublishAsyncTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    private readonly ITestOutputHelper _output = output;

    public interface IE2eEvent {}
    public record E2eEvent(string Name) : IE2eEvent;
    public record E2eCommand(string Name) : ICommand;

    public class EventCollector
    {
        private readonly List<string> _events = new();
        private readonly ILogger<EventCollector> _logger;

        public EventCollector(ILogger<EventCollector> logger)
        {
            _logger = logger;
            _logger.LogInformation("Started EventCollector with Id: {Id}", Id);
        }

        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 7);
        public IReadOnlyCollection<string> Events => _events;

        public void AddEvent(string eventName)
        {
            _logger.LogInformation("Collector {Id} adding event: {EventName}", Id, eventName);
            _events.Add(eventName);
        }

        public void Reset() => _events.Clear();
    }

    public class FirstConsumer(EventCollector collector, ILogger<FirstConsumer> logger)
    {
        public Task HandleAsync(IE2eEvent message, CancellationToken ct)
        {
            logger.LogInformation("Handling interface event: {EventType}", message.GetType().Name);
            collector.AddEvent("first:" + message.GetType().Name); return Task.CompletedTask;
        }
    }

    public class SecondConsumer(EventCollector collector, ILogger<SecondConsumer> logger)
    {
        public Task HandleAsync(E2eEvent message, CancellationToken ct)
        {
            logger.LogInformation("Handling event: {EventName}", message.Name);
            collector.AddEvent("second:" + message.Name); return Task.CompletedTask;
        }
    }

    public class CascadingConsumer(EventCollector collector, ILogger<CascadingConsumer> logger)
    {
        public Task<(Result, E2eEvent)> HandleAsync(E2eCommand command, CancellationToken ct) {
            logger.LogInformation("Handling command: {CommandName}", command.Name);
            collector.AddEvent("cascading:" + command.Name);
            return Task.FromResult((Result.Success(), new E2eEvent(command.Name)));
        }
    }

    [Fact]
    public async Task PublishAsync_FansOut()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o => o.UseOutputHelper(() => _output)));
        services.AddSingleton<EventCollector>();
        services.AddMediator(b => b.AddAssembly<E2eEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var collector = provider.GetRequiredService<EventCollector>();

        await mediator.PublishAsync(new E2eEvent("evt"));

        Assert.Collection(collector.Events,
            e => Assert.Equal("second:evt", e),
            e => Assert.Equal("first:E2eEvent", e));
    }

    [Fact]
    public async Task CascadingPublishAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(c => c.AddTestLogger(o =>
        {
            o.UseOutputHelper(() => _output);
        }));
        services.AddSingleton<EventCollector>();
        services.AddMediator(b => b.AddAssembly<E2eEvent>());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var collector = provider.GetRequiredService<EventCollector>();

        var result = await mediator.InvokeAsync<Result>(new E2eCommand("command"));
        Assert.True(result.IsSuccess);

        Assert.Collection(collector.Events,
            e => Assert.Equal("cascading:command", e),
            e => Assert.Equal("second:command", e),
            e => Assert.Equal("first:E2eEvent", e));

        collector.Reset();

        object objectResult = await mediator.InvokeAsync<object>(new E2eCommand("command"));
        Assert.True(result.IsSuccess);
        Assert.IsType<Result>(objectResult);

        Assert.Collection(collector.Events,
            e => Assert.Equal("cascading:command", e),
            e => Assert.Equal("second:command", e),
            e => Assert.Equal("first:E2eEvent", e));
    }
}
