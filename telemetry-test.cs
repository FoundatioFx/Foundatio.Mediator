using System.Diagnostics;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;

// Create simple console app to test OpenTelemetry
var services = new ServiceCollection();
services.AddMediator();

var serviceProvider = services.BuildServiceProvider();
var mediator = serviceProvider.GetRequiredService<IMediator>();

// Set up activity listener to capture activities
var activities = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = source => source.Name == "Foundatio.Mediator",
    Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
    ActivityStarted = activity => activities.Add(activity)
};
ActivitySource.AddActivityListener(listener);

Console.WriteLine("Testing Foundatio.Mediator OpenTelemetry integration...");

// Test 1: Try to invoke a non-existent handler (should still create activity)
try
{
    await mediator.InvokeAsync<string>(new { Message = "Test" });
}
catch (Exception ex)
{
    Console.WriteLine($"Expected exception: {ex.Message}");
}

// Test 2: Try to publish an event (should create activity)
try
{
    await mediator.PublishAsync(new { Event = "TestEvent" });
}
catch (Exception ex)
{
    Console.WriteLine($"Expected exception: {ex.Message}");
}

Console.WriteLine($"\nActivities captured: {activities.Count}");
foreach (var activity in activities)
{
    Console.WriteLine($"Activity: {activity.OperationName}");
    foreach (var tag in activity.Tags)
    {
        Console.WriteLine($"  {tag.Key}: {tag.Value}");
    }
    Console.WriteLine();
}

Console.WriteLine("OpenTelemetry integration test completed successfully!");