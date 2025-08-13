using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Mediator.Tests;

public class OpenTelemetryDisabledTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ActivitySource_DoesNotCreateActivity_WhenDisabled()
    {
        // This test verifies that when EnableMediatorOpenTelemetry=false is set,
        // activities are not created. Since we can't change compile-time properties
        // in a test, we'll just verify that the conditional compilation works
        // by checking if the activity source field exists
        
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Foundatio.Mediator",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var services = new ServiceCollection();
        services.AddMediator();
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<IMediator>();

        try
        {
            await mediator.PublishAsync(new { Event = "Test" });
        }
        catch
        {
            // Ignore errors, we just want to see if activities are created
        }

        // Verify that the activity source actually exists (compilation check)
        var mediatorType = typeof(Mediator);
        var activitySourceField = mediatorType.GetField("ActivitySource", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
#if DISABLE_MEDIATOR_OPENTELEMETRY
        // If OpenTelemetry is disabled, the field should not exist
        Assert.Null(activitySourceField);
        Assert.Empty(activities);
#else
        // If OpenTelemetry is enabled (default), the field should exist
        Assert.NotNull(activitySourceField);
        var mediatorActivity = activities.FirstOrDefault(a => a.OperationName == "mediator.publish");
        Assert.NotNull(mediatorActivity);
#endif
        
        output.WriteLine($"Activities found: {activities.Count}");
        foreach (var activity in activities)
        {
            output.WriteLine($"  {activity.OperationName}");
        }
    }
}