using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Foundatio.Mediator.Distributed.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using StackExchange.Redis.Profiling;

int count = args.Length > 0 ? int.Parse(args[0]) : 5000;
ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
string? redis = args.Length > 1 ? args[1] : null;
bool profileRedis = args.Length > 2 && args[2] == "--profile";
await RunAsync(100, tracked: false, redis: null, report: false); // JIT warm-up
await RunAsync(100, tracked: true, redis: null, report: false);
await RunAsync(count, tracked: false, redis: null, report: true);
await RunAsync(count, tracked: true, redis: null, report: true);
if (redis is not null)
{
    await RunAsync(100, tracked: true, redis: redis, report: false);
    await RunAsync(count, tracked: true, redis: redis, report: true, profileRedis);
}

static async Task RunAsync(int count, bool tracked, string? redis, bool report, bool profileRedis = false)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    await using var client = new LoadClient(count);
    builder.Services.AddSingleton<IQueueClient>(client);
    var mediatorBuilder = builder.Services.AddMediator(options => options.AddAssembly<LoadCommandHandler>())
        .AddDistributedQueues(options =>
        {
            options.QueueDepthPollInterval = TimeSpan.Zero;
            options.JobStateExpiry = TimeSpan.FromMinutes(5);
            options.QueueOverrides["load"] = queue => queue.TrackProgress = tracked;
        });
    using var connection = redis is null ? null : await ConnectionMultiplexer.ConnectAsync(redis);
    if (connection is not null)
    {
        builder.Services.AddSingleton<IConnectionMultiplexer>(connection);
        mediatorBuilder.UseRedisJobState(options =>
        {
            options.KeyPrefix = "load:" + Guid.NewGuid().ToString("N");
            options.NonTerminalExpiry = TimeSpan.FromMinutes(5);
        });
    }
    using var host = builder.Build();
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    await host.StartAsync(timeout.Token);
    try
    {
        var mediator = host.Services.GetRequiredService<IMediator>();
        var profiling = profileRedis && connection is not null ? new ProfilingSession() : null;
        if (profiling is not null) connection!.RegisterProfiler(() => profiling);
        GC.Collect();
        long allocated = GC.GetTotalAllocatedBytes(true);
        long redisBefore = connection?.OperationCount ?? 0;
        var stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < count; index++)
        {
            // One producer; capture before validation/state creation as well as transport send.
            client.EnqueueStarted = Stopwatch.GetTimestamp();
            var acceptance = await mediator.EnqueueAsync(new LoadCommand(index), timeout.Token);
            if (!acceptance.IsSuccess) throw new InvalidOperationException(acceptance.Message);
        }
        await client.Finished.Task.WaitAsync(timeout.Token);
        stopwatch.Stop();
        long totalAllocated = GC.GetTotalAllocatedBytes(true) - allocated;
        var commands = profiling?.FinishProfiling().ToArray();
        var durations = client.Durations.Order().ToArray();
        if (client.MaxInFlight > 8 || durations.Length != count || client.Completions != count)
            throw new InvalidOperationException("Load run did not preserve concurrency and completion invariants.");
        if (report)
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                State = tracked ? redis is null ? "memory" : "redis" : "untracked",
                Messages = count, Concurrency = 8,
                MessagesPerSecond = Math.Round(count / stopwatch.Elapsed.TotalSeconds),
                P50Milliseconds = Math.Round(durations[durations.Length / 2], 3),
                P95Milliseconds = Math.Round(durations[(int)((durations.Length - 1) * .95)], 3),
                AllocatedBytesPerMessage = totalAllocated / count,
                client.SendCalls, client.ReceiveCalls, client.Completions, client.Renewals, client.MaxInFlight,
                RedisOperations = (connection?.OperationCount ?? 0) - redisBefore,
                RedisProfiledCommands = commands?.Length,
                RedisMeanRoundTripMilliseconds = commands is { Length: > 0 }
                    ? Math.Round(commands.Average(command => command.SentToResponse.TotalMilliseconds), 3) : (double?)null
            }));
    }
    finally { await host.StopAsync(CancellationToken.None); }
}

public record LoadCommand(int Index);

[Queue(QueueName = "load", Concurrency = 8, PrefetchCount = 32)]
public class LoadCommandHandler
{
    public Result Handle(LoadCommand message) => Result.Ok();
}

// The probe accounts for completion after worker state updates, avoiding a polling delay in timings.
sealed class LoadClient(int expected) : IQueueClient, IQueueProcessingObserver
{
    private readonly InMemoryQueueClient _inner = new();
    private readonly ConcurrentDictionary<string, long> _started = new();
    private int _inFlight;
    private int _maxInFlight;
    private int _finished;
    private long _sends, _receives, _completions, _renewals;
    public bool IsDistributed => false;
    public long EnqueueStarted { get; set; }
    public ConcurrentBag<double> Durations { get; } = [];
    public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public long SendCalls => Interlocked.Read(ref _sends);
    public long ReceiveCalls => Interlocked.Read(ref _receives);
    public long Completions => Interlocked.Read(ref _completions);
    public long Renewals => Interlocked.Read(ref _renewals);
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);
    private static string Key(IReadOnlyDictionary<string, string> headers) => headers.GetValueOrDefault(MessageHeaders.JobId) ?? headers[MessageHeaders.CorrelationId];
    public Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _sends);
        foreach (var entry in entries)
            if (!_started.TryAdd(Key(entry.Headers!), EnqueueStarted)) throw new InvalidOperationException("Probe requires unique job/correlation IDs.");
        return _inner.SendAsync(queueName, entries, cancellationToken);
    }
    public async Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, TimeSpan? visibilityTimeout, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _receives);
        var messages = await _inner.ReceiveAsync(queueName, maxCount, visibilityTimeout, cancellationToken);
        int active = Interlocked.Add(ref _inFlight, messages.Count);
        int observed;
        do { observed = Volatile.Read(ref _maxInFlight); }
        while (active > observed && Interlocked.CompareExchange(ref _maxInFlight, active, observed) != observed);
        return messages;
    }
    public void ProcessingFinished(QueueMessage message)
    {
        if (!_started.TryRemove(Key(message.Headers), out var start))
        {
            Finished.TrySetException(new InvalidOperationException("Unknown completion."));
            return;
        }
        Durations.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        Interlocked.Decrement(ref _inFlight);
        if (Interlocked.Increment(ref _finished) == expected) Finished.TrySetResult();
    }
    public async Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default)
    {
        await _inner.CompleteAsync(message, cancellationToken);
        Interlocked.Increment(ref _completions);
    }
    public Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default) => _inner.AbandonAsync(message, delay, cancellationToken);
    public Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _renewals);
        return _inner.RenewTimeoutAsync(message, extension, cancellationToken);
    }
    public Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default) => throw new InvalidOperationException(reason);
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
