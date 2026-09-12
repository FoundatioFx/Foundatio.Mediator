using StackExchange.Redis;
using Foundatio.Jobs;
using Amazon.Runtime;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Foundatio;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Foundatio.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// A solution can otherwise copy Debug-built external references into a Release output directory.
var assemblies = new[] { typeof(MessageBus).Assembly, typeof(IMediator).Assembly, typeof(QueueWorker).Assembly, Assembly.GetExecutingAssembly() };
foreach (var assembly in assemblies)
    if (assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true)
        throw new InvalidOperationException($"{assembly.GetName().Name} was built without optimizations. Rebuild the solution with -c Release before benchmarking.");

string layer = ReadString("--layer", "mediator");
if (layer is not ("transport" or "bus" or "execution" or "mediator"))
    throw new ArgumentException("--layer must be transport, bus, execution, or mediator.");
if (layer != "mediator" && (args.Contains("--tracking") || args.Contains("--aws") || args.Contains("--redis")))
    throw new ArgumentException("Layer isolation uses untracked in-memory delivery.");
int count = ReadInt("--count", 20_000);
int concurrency = ReadInt("--concurrency", 64);
bool tracking = args.Contains("--tracking");
bool redis = args.Contains("--redis");
int completedExpected = 0;
bool aws = args.Contains("--aws");
string serviceUrl = Environment.GetEnvironmentVariable("BENCHMARK_AWS_URL") ?? "http://127.0.0.1:14566";
if (aws && (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var endpoint) || !endpoint.IsLoopback))
    throw new ArgumentException("The AWS comparison accepts only a local emulator endpoint.");
string prefix = "compare-" + Guid.NewGuid().ToString("N");
string queueName = prefix + "-comparison";
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
if (redis) builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("BENCHMARK_REDIS_CONNECTION_STRING") ?? "localhost:6379"));
var foundatio = builder.Services.AddFoundatio();
if (redis) foundatio.Jobs.UseRedis(options => options.KeyPrefix = prefix + ":jobs:");
else if (!args.Contains("--no-store")) foundatio.Jobs.UseInMemory();
var messaging = foundatio.Messaging;
if (aws) messaging.UseAws(options => { options.ServiceUrl = serviceUrl; options.Credentials = new BasicAWSCredentials("test", "test"); });
else messaging.UseInMemory();
builder.Services.AddSingleton<Measurements>();
if (layer == "mediator") builder.Services.AddMediator(options => options.AddAssembly<ComparisonHandler>())
    .ConfigureDistributed(options => options.ResourcePrefix = prefix)
    .AddDistributedQueues(options =>
{
    options.QueueDepthPollInterval = TimeSpan.Zero;
    options.ReceiveBatchDelay = TimeSpan.FromMilliseconds(args.Contains("--default-delay") ? 1 : 0);
    options.QueueOverrides["comparison"] = queue => { queue.Concurrency = concurrency; queue.TrackProgress = tracking; queue.AutoRenewTimeout = !args.Contains("--no-renew"); };
});
using var host = builder.Build();
await host.StartAsync();
var mediator = host.Services.GetService<IMediator>();
var bus = host.Services.GetRequiredService<IMessageBus>();
var transport = host.Services.GetRequiredService<IMessageTransport>();
var measurements = host.Services.GetRequiredService<Measurements>();
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var administration = new MessageAdministration(host.Services.GetRequiredService<IMessageTransport>());
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
IMessageSubscription? consumer = null;
Task? receiver = null;
using var receiving = new CancellationTokenSource();
if (layer == "transport") receiver = Task.Run(ReceiveTransportAsync);
else if (layer is "bus" or "execution")
{
    var pipeline = new MessageExecutionPipeline(new MessageExecutionOptions { QueueName = queueName, MessageType = typeof(ComparisonWork) });
    consumer = await bus.ConsumeAsync((context, token) => layer == "execution"
        ? pipeline.ProcessAsync(context, (processing, _) => { RecordBody(processing.Body); return new(MessageOutcome.Success); }, token)
        : RecordDeliveryAsync(context), new MessageConsumerOptions
        {
            Destination = queueName, MaxConcurrency = concurrency, ReceiveBatchDelay = TimeSpan.Zero
        }, receiving.Token);
}
try
{
    await RunAsync(Math.Min(1000, count));
    await Task.Delay(50, timeout.Token);
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long allocated = GC.GetTotalAllocatedBytes(true);
    int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
    var cpu = Process.GetCurrentProcess().TotalProcessorTime;
    long started = Stopwatch.GetTimestamp();
    var accepts = await RunAsync(count);
    var elapsed = Stopwatch.GetElapsedTime(started);
    var output = new
    {
        Implementation = "foundatio-native",
        Layer = layer,
        Transport = aws ? "localstack" : "memory",
        Messages = count,
        Concurrency = concurrency,
        Tracking = tracking,
        Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        TrackingStore = redis ? "redis" : "memory",
        Gen0Collections = GC.CollectionCount(0) - gen0, Gen1Collections = GC.CollectionCount(1) - gen1, Gen2Collections = GC.CollectionCount(2) - gen2,
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
    await receiving.CancelAsync();
    if (consumer is not null) await consumer.DisposeAsync();
    if (receiver is not null)
    {
        try { await receiver; }
        catch (OperationCanceledException) when (receiving.IsCancellationRequested) { }
    }
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
        var work = new ComparisonWork(index, payload);
        if (layer == "mediator")
        {
            var result = await mediator!.EnqueueAsync(work, token);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
        }
        else if (layer == "transport")
        {
            var result = await transport.SendAsync(DestinationAddress.ForQueue(queueName),
                [new TransportMessage { Body = JsonSerializer.SerializeToUtf8Bytes(work), ContentType = "application/json" }], new(), token);
            if (result.Items.Count != 1 || result.Items[0].Status != MessageSendStatus.Accepted)
                throw new InvalidOperationException("Transport rejected a benchmark message.");
        }
        else await bus.SendAsync(work, new MessageSendOptions { Destination = queueName }, token);
        accepts[index] = Stopwatch.GetElapsedTime(measurements.Starts[index]).TotalMilliseconds;
    });
    await measurements.Completion.Task.WaitAsync(timeout.Token);
    while (true)
    {
        var stats = await administration.GetStatsAsync(DestinationAddress.ForQueue(queueName), timeout.Token);
        if (stats.Queued == 0 && stats.Working == 0) break;
        await Task.Delay(1, timeout.Token);
    }
    completedExpected += size;
    if (tracking)
    {
        while (await host.Services.GetRequiredService<IJobRuntimeStore>().CountAsync(new JobQuery { QueueName = queueName, Status = JobStatus.Completed }, timeout.Token) != completedExpected)
            await Task.Delay(1, timeout.Token);
    }
    return accepts;
}
async Task ReceiveTransportAsync()
{
    var pull = (ISupportsPull)transport;
    var destination = DestinationAddress.ForQueue(queueName);
    var request = new ReceiveRequest { MaxMessages = concurrency, MaxWaitTime = TimeSpan.FromSeconds(1) };
    while (!receiving.IsCancellationRequested)
    {
        var entries = await pull.ReceiveAsync(destination, request, receiving.Token);
        foreach (var entry in entries)
        {
            RecordBody(entry.Body);
            await transport.CompleteAsync(entry, receiving.Token);
        }
    }
}
Task RecordDeliveryAsync(IMessageContext context)
{
    RecordBody(context.Body);
    return Task.CompletedTask;
}
void RecordBody(ReadOnlyMemory<byte> body) => measurements.Record(JsonSerializer.Deserialize<ComparisonWork>(body.Span, jsonOptions)!.Index);
string ReadString(string name, string fallback)
{
    int index = Array.IndexOf(args, name);
    if (index < 0) return fallback;
    if (index + 1 >= args.Length) throw new ArgumentException($"{name} requires a value.");
    return args[index + 1];
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
