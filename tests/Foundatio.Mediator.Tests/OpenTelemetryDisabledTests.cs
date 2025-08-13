using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public record TestDisabledPing(string Message) : IQuery;

public class TestDisabledPingHandler
{
    public Task<string> HandleAsync(TestDisabledPing message, CancellationToken ct) => Task.FromResult(message.Message + " Pong");
}

public class OpenTelemetryDisabledTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ActivitySource_DoesNotCreateActivity_WhenDisabled()
    {
        // This test verifies that when EnableMediatorOpenTelemetry=false is set,
        // activities are not created. Since the ActivitySource is now in generated code,
        // we check if activities are created when handlers are called
        
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Foundatio.Mediator",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<TestDisabledPingHandler>());
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        // Call a handler that should generate code with or without OpenTelemetry based on build settings
        var result = await mediator.InvokeAsync<string>(new TestDisabledPing("Test"));
        Assert.Equal("Test Pong", result);

#if DISABLE_MEDIATOR_OPENTELEMETRY
        // If OpenTelemetry is disabled, no activities should be created
        var mediatorActivity = activities.FirstOrDefault(a => a.OperationName == "mediator.invoke");
        Assert.Null(mediatorActivity);
#else
        // If OpenTelemetry is enabled (default), activities should be created
        var mediatorActivity = activities.FirstOrDefault(a => a.OperationName == "mediator.invoke");
        Assert.NotNull(mediatorActivity);
#endif
        
        output.WriteLine($"Activities found: {activities.Count}");
        foreach (var activity in activities)
        {
            output.WriteLine($"  {activity.OperationName}");
        }
    }
}