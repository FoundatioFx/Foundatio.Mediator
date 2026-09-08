using Amazon.Runtime;
using System.Diagnostics;
using System.Text.Json;
using Foundatio;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Foundatio.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

int count = ReadInt("--count", 20_000);
int concurrency = ReadInt("--concurrency", 64);
bool tracking = args.Contains("--tracking");
bool aws = args.Contains("--aws");
string serviceUrl = Environment.GetEnvironmentVariable("BENCHMARK_AWS_URL") ?? "http://127.0.0.1:14566";
if (aws && (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback))
    throw new ArgumentException("The AWS comparison accepts only a local emulator endpoint.");
string prefix = "compare-" + Guid.NewGuid().ToString("N");
string queueName = prefix + "-comparison";
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
var foundatio = builder.Services.AddFoundatio();
foundatio.Jobs.UseInMemory();
var messaging = foundatio.Messaging;
if (aws) messaging.UseAws(options => { options.ServiceUrl = serviceUrl; options.Credentials = new BasicAWSCredentials("test", "test"); });
else messaging.UseInMemory();
builder.Services.AddSingleton<Measurements>();
builder.Services.AddMediator(options => options.AddAssembly<ComparisonHandler>())
    .ConfigureDistributed(options => options.ResourcePrefix = prefix)
    .AddDistributedQueues(options =>
{
    options.QueueDepthPollInterval = TimeSpan.Zero;
    options.ReceiveBatchDelay = TimeSpan.Zero;
    options.QueueOverrides["comparison"] = queue => { queue.Concurrency = concurrency; queue.TrackProgress = tracking; };
});
using var host = builder.Build();
await host.StartAsync();
var mediator = host.Services.GetRequiredService<IMediator>();
var measurements = host.Services.GetRequiredService<Measurements>();
var administration = new MessageAdministration(host.Services.GetRequiredService<IMessageTransport>());
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
try
{
    await RunAsync(Math.Min(1000, count));
    await Task.Delay(50, timeout.Token);
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long allocated = GC.GetTotalAllocatedBytes(true);
    var cpu = Process.GetCurrentProcess().TotalProcessorTime;
    long started = Stopwatch.GetTimestamp();
    var accepts = await RunAsync(count);
    var elapsed = Stopwatch.GetElapsedTime(started);
    var output = new
    {
        Implementation = "foundatio-native",
        Transport = aws ? "localstack" : "memory",
        Messages = count,
        Concurrency = concurrency,
        Tracking = tracking,
        Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        ElapsedMilliseconds = elapsed.TotalMilliseconds,
        MessagesPerSecond = count / elapsed.TotalSeconds,
        AllocatedBytesPerMessage = (GC.GetTotalAllocatedBytes(true) - allocated) / (double)count,
        CpuMilliseconds = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds,
        AcceptanceP50Milliseconds = Percentile(accepts, .50),
        AcceptanceP99Milliseconds = Percentile(accepts, .99),
        HandlerCompletionP50Milliseconds = Percentile(measurements.CompletionMilliseconds, .50),
        HandlerCompletionP99Milliseconds = Percentile(measurements.CompletionMilliseconds, .99),
        UniqueProcessed = measurements.UniqueProcessed,
        Duplicates = measurements.Duplicates
    };
    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    await host.StopAsync(timeout.Token);
    if (aws)
    {
        var provisioning = (ISupportsProvisioning)host.Services.GetRequiredService<IMessageTransport>();
        await provisioning.DeleteAsync(DestinationAddress.ForQueue(queueName), timeout.Token);
        await provisioning.DeleteAsync(DestinationAddress.ForQueue(queueName + ".deadletter"), timeout.Token);
    }
}

async Task<double[]> RunAsync(int size)
{
    measurements.Begin(size);
    var accepts = new double[size];
    var payload = new string('x', 256);
    await Parallel.ForEachAsync(Enumerable.Range(0, size), new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = timeout.Token }, async (index, token) =>
    {
        measurements.Starts[index] = Stopwatch.GetTimestamp();
        var result = await mediator.EnqueueAsync(new ComparisonWork(index, payload), token);
        if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
        accepts[index] = Stopwatch.GetElapsedTime(measurements.Starts[index]).TotalMilliseconds;
    });
    await measurements.Completion.Task.WaitAsync(timeout.Token);
    while (true)
    {
        var stats = await administration.GetStatsAsync(DestinationAddress.ForQueue(queueName), timeout.Token);
        if (stats.Queued == 0 && stats.Working == 0) break;
        await Task.Delay(1, timeout.Token);
    }
    return accepts;
}
int ReadInt(string name, int fallback)
{
    int index = Array.IndexOf(args, name);
    if (index < 0) return fallback;
    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int value) || value < 1)
        throw new ArgumentException($"{name} requires a positive integer.");
    return value;
}
static double Percentile(double[] values, double percentile)
{
    Array.Sort(values);
    return values[Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1)];
}
public record ComparisonWork(int Index, string Payload);
[Queue(QueueName = "comparison", Concurrency = 64)]
public class ComparisonHandler(Measurements measurements)
{
    public void Handle(ComparisonWork message) => measurements.Record(message.Index);
}
public sealed class Measurements
{
    private int[] _seen = [];
    private int _processed;
    private int _duplicates;
    public long[] Starts { get; private set; } = [];
    public double[] CompletionMilliseconds { get; private set; } = [];
    public TaskCompletionSource Completion { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int UniqueProcessed => Volatile.Read(ref _processed);
    public int Duplicates => Volatile.Read(ref _duplicates);
    public void Begin(int count)
    {
        _seen = new int[count]; Starts = new long[count]; CompletionMilliseconds = new double[count];
        _processed = 0; _duplicates = 0; Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public void Record(int index)
    {
        if (Interlocked.Exchange(ref _seen[index], 1) != 0) { Interlocked.Increment(ref _duplicates); return; }
        CompletionMilliseconds[index] = Stopwatch.GetElapsedTime(Starts[index]).TotalMilliseconds;
        if (Interlocked.Increment(ref _processed) == _seen.Length) Completion.TrySetResult();
    }
}
