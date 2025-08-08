using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_PublishAsyncTests
{
    public interface IE2eEvent {}
    public record E2eEvent(string Name) : IE2eEvent;

    public class EventCollector
    {
        public List<string> Events { get; } = new();
    }

    public class FirstConsumer
    {
        private readonly EventCollector _collector;
        public FirstConsumer(EventCollector collector) => _collector = collector;
        public Task HandleAsync(IE2eEvent message, CancellationToken ct) { _collector.Events.Add("first:" + message.GetType().Name); return Task.CompletedTask; }
    }

    public class SecondConsumer
    {
        private readonly EventCollector _collector;
        public SecondConsumer(EventCollector collector) => _collector = collector;
        public Task HandleAsync(E2eEvent message, CancellationToken ct) { _collector.Events.Add("second:" + message.Name); return Task.CompletedTask; }
    }

    [global::Xunit.Fact]
    public async Task PublishAsync_FansOut()
    {
        var services = new ServiceCollection();
    services.AddSingleton<EventCollector>();
    // Both consumers are in the same assembly; add it once to avoid duplicate registrations
    services.AddMediator(b => b.AddAssembly<E2eEvent>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        var collector = provider.GetRequiredService<EventCollector>();

        await mediator.PublishAsync(new E2eEvent("evt"));

        collector.Events.Should().BeEquivalentTo(new[]{"first:E2eEvent","second:evt"});
    }
}
