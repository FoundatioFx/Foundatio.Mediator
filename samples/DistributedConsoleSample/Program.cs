using Foundatio.Messaging;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Foundatio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddFoundatio().Messaging.UseInMemory().UseInMemoryExecutionTracking();
builder.Services.AddMediator(options => options.AddAssembly<GenerateGreetingHandler>())
    .ConfigureDistributed(options => options.ResourcePrefix = "demo")
    .AddDistributedQueues();
using var host = builder.Build();
await host.StartAsync();
try
{
    var mediator = host.Services.GetRequiredService<IMediator>();
    var rejected = await mediator.EnqueueAsync(new GenerateGreeting(""));
    Console.WriteLine($"Empty name: {rejected.Status}");

    var accepted = await mediator.EnqueueAsync(new GenerateGreeting("World"));
    if (!accepted.IsSuccess)
        throw new InvalidOperationException(accepted.Message);
    var receipt = accepted.Value;
    Console.WriteLine($"Accepted {receipt.JobId} on {receipt.QueueName}");

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var state = await host.Services.GetRequiredService<IMessageExecutionStore>().WaitForCompletionAsync(receipt.JobId!, timeout.Token);
    Console.WriteLine($"Job {state!.Status}: {state.ProgressMessage}");
    if (state.Status != MessageExecutionStatus.Completed)
        throw new InvalidOperationException("The greeting job did not complete.");
}
finally { await host.StopAsync(); }

public record GenerateGreeting(string Name);

[Middleware(Stage = MiddlewareStage.Enqueue)]
public class GreetingValidationMiddleware
{
    public HandlerResult Before(GenerateGreeting message)
        => string.IsNullOrWhiteSpace(message.Name)
            ? HandlerResult.ShortCircuit(Result.Invalid("Name is required"))
            : HandlerResult.ContinueWith(message with { Name = message.Name.Trim() });
}

[Queue(TrackProgress = true)]
public class GenerateGreetingHandler
{
    public async Task<Result> HandleAsync(GenerateGreeting message, MessageProcessingContext context, CancellationToken ct)
    {
        await context.ReportProgressAsync(100, $"Hello, {message.Name}!", ct);
        return Result.Ok();
    }
}
