using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Foundatio;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Foundatio.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

string mode = args.FirstOrDefault() ?? "paced";
int capacity = args.Length > 1 ? int.Parse(args[1]) : 1000;
bool bridge = mode != "local-off";
bool local = mode.StartsWith("local-");
int count = local ? 1000000 : 10000;
string prefix = "notify-review-" + Guid.NewGuid().ToString("N");
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
await using var transport = new InMemoryMessageTransport();
using var first = await StartAsync(bridge);
using var second = local ? null : await StartAsync(true);
var mediator = first.Services.GetRequiredService<IMediator>();
var remote = second?.Services.GetRequiredService<ReceiveLog>();
if (local)
{
    for (int i = 0; i < 1000; i++) await mediator.PublishAsync(new LocalEvent(i), timeout.Token);
}
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
long allocated = GC.GetTotalAllocatedBytes(true);
long started = Stopwatch.GetTimestamp();
for (int i = 0; i < count; i++)
{
    if (local) await mediator.PublishAsync(new LocalEvent(i), timeout.Token);
    else
    {
        await mediator.PublishAsync(new ClusterEvent(i), timeout.Token);
        if (mode == "mixed")
            for (int j = 0; j < 100; j++) await mediator.PublishAsync(new LocalEvent(j), timeout.Token);
        if (mode == "paced" && i % 100 == 99)
            while (remote!.Seen.Count < i + 1) await Task.Delay(1, timeout.Token);
    }
}
var publishElapsed = Stopwatch.GetElapsedTime(started);
long allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated;
if (remote is not null)
{
    // The counter is updated by the handler itself, so the measurement has no subscriber buffer of its own.
    await Task.Delay(1000, timeout.Token);
    int previous = remote.Seen.Count;
    await Task.Delay(500, timeout.Token);
    if (remote.Seen.Count != previous) throw new InvalidOperationException("Remote delivery has not drained.");
}
Console.WriteLine(JsonSerializer.Serialize(new {
    Mode = mode, Capacity = capacity, Count = count, LocalOnlyEvents = local ? count : mode == "mixed" ? count * 100 : 0,
    PublisherMilliseconds = publishElapsed.TotalMilliseconds, AllocatedBytesPerPublish = allocatedBytes / (double)(local ? count : mode == "mixed" ? count * 101 : count),
    RemoteUnique = remote?.Seen.Count, Duplicates = remote?.Duplicates,
    SenderUnique = first.Services.GetRequiredService<ReceiveLog>().Seen.Count
}));
if (second is not null) await second.StopAsync(timeout.Token);
await first.StopAsync(timeout.Token);

async Task<IHost> StartAsync(bool enabled)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Services.AddSingleton<ReceiveLog>();
    builder.Services.AddFoundatio().Messaging.UseTransport(transport);
    var services = builder.Services.AddMediator(options => options.AddAssembly<ClusterEventHandler>());
    if (enabled) services.ConfigureDistributed(options => options.ResourcePrefix = prefix)
        .AddDistributedNotifications(options => { options.Include<ClusterEvent>(); options.MaxCapacity = capacity; });
    var host = builder.Build();
    await host.StartAsync(timeout.Token);
    return host;
}

public sealed record LocalEvent(int Id) : INotification;
public sealed record ClusterEvent(int Id) : INotification;
public sealed class ReceiveLog
{
    public ConcurrentDictionary<int, byte> Seen { get; } = new();
    public int Duplicates;
}
public sealed class ClusterEventHandler(ReceiveLog log)
{
    public void Handle(ClusterEvent message)
    {
        if (!log.Seen.TryAdd(message.Id, 0)) Interlocked.Increment(ref log.Duplicates);
    }
}
