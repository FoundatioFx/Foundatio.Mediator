using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Foundatio.Mediator.Distributed;

namespace DistributedBenchmarks;

internal static class Scenario
{
    public static async Task<RunResult> RunAsync(Settings settings, string settingsPath)
    {
        settings.Validate();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        var ct = timeout.Token;
        using var counters = new TransportCounters();
        using var broker = settings.Broker ? new BrokerResources(settings) : null;
        await using var memoryBus = new InMemoryPubSubClient();
        var drivers = new List<Driver>();
        var children = new List<ConsumerProcess>();
        var collectors = new List<DeliveryCollector>();
        var startedUtc = DateTimeOffset.UtcNow;
        long start = 0, apiEnd = 0, drained = 0, publishedBefore = 0, droppedBefore = 0;
        int sent = 0;
        string? error = null;
        var apiLatency = new long[settings.Count];
        var reports = Array.Empty<NodeSnapshot>();
        var costBefore = ProcessCost.Read();
        ProcessCost? producerCost = null;
        try
        {
            if (settings.Broker)
                for (int i = 0; i < settings.Subscribers; i++) children.Add(await ConsumerProcess.StartAsync(settingsPath, i, ct));
            else if (settings.Framework == "foundatio" && settings.Operation == "pubsub" && settings.Transport != "local")
                for (int i = 0; i < settings.Fanout; i++)
                {
                    var consumer = new Driver(settings, consumer: true, i, memoryBus);
                    drivers.Add(consumer);
                    await consumer.StartAsync(ct);
                    collectors.AddRange(consumer.Collectors);
                }

            bool consumesLocally = !settings.Broker && !(settings.Framework == "foundatio" && settings.Operation == "pubsub" && settings.Transport != "local");
            var producer = new Driver(settings, consumer: consumesLocally, sharedBus: memoryBus);
            drivers.Add(producer);
            await producer.StartAsync(ct);
            if (consumesLocally) collectors.AddRange(producer.Collectors);

            string payload = new('x', settings.PayloadBytes);
            await SendAsync(producer, settings, warmup: true, payload, null, () => { }, ct);
            await WaitForDeliveryAsync(settings.Warmup, warmup: true, ct);
            if (broker is not null) await broker.DrainAsync(ct);
            if (counters.Dropped != 0 || counters.Failures != 0) throw new InvalidOperationException("Warmup dropped or failed messages; increase the buffer or reduce warmup.");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            foreach (var child in children) await child.RequestAsync("begin", ct);
            foreach (var collector in collectors) collector.Begin();
            publishedBefore = counters.Published;
            droppedBefore = counters.Dropped;
            costBefore = ProcessCost.Read();
            start = Stopwatch.GetTimestamp();
            await SendAsync(producer, settings, warmup: false, payload, apiLatency, () => Interlocked.Increment(ref sent), ct, start);
            apiEnd = Stopwatch.GetTimestamp();

            if (settings.Framework == "foundatio" && settings.Operation == "pubsub" && settings.Transport != "local")
                while (counters.Published - publishedBefore + counters.Dropped - droppedBefore < sent) await Task.Delay(10, ct);
            int expectedPerSubscriber = sent - checked((int)(counters.Dropped - droppedBefore));
            await WaitForDeliveryAsync(expectedPerSubscriber, warmup: false, ct);
            if (broker is not null) await broker.DrainAsync(ct);
            drained = Stopwatch.GetTimestamp();
        }
        catch (Exception ex)
        {
            error = ex is OperationCanceledException ? $"Timed out after {settings.TimeoutSeconds}s." : ex.ToString();
        }
        finally
        {
            // Capture measurements before shutdown and broker cleanup. Preserve a failed run's partial counts.
            producerCost = ProcessCost.Read().Since(costBefore);
            using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var snapshots = new List<NodeSnapshot>();
            foreach (var child in children)
            {
                try { snapshots.Add(await child.RequestAsync("report", reportTimeout.Token)); }
                catch (Exception ex) { error ??= "Consumer report failed: " + ex.Message; }
            }
            snapshots.Add(new NodeSnapshot(collectors.Select(c => c.Snapshot(false, true)).ToArray(), counters.Processed, counters.Failures));
            reports = snapshots.ToArray();
            foreach (var driver in drivers.AsEnumerable().Reverse())
            {
                try { await driver.DisposeAsync(); }
                catch (Exception ex) { error ??= "Host shutdown failed: " + ex.Message; }
            }
            foreach (var child in children) await child.DisposeAsync();
            if (broker is not null)
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await broker.CleanupAsync(cleanupTimeout.Token); }
                catch (Exception ex) { error ??= $"Cleanup failed for {settings.RunId}: {ex.Message}"; }
            }
        }

        var deliveries = reports.SelectMany(r => r.Deliveries).ToArray();
        int delivered = deliveries.Sum(d => d.Received);
        int duplicates = deliveries.Sum(d => d.Duplicates);
        int invalid = deliveries.Sum(d => d.Invalid);
        int failures = reports.Sum(r => r.Failures);
        long dropped = counters.Dropped - droppedBefore;
        double seconds = start > 0 && delivered > 0 ? (deliveries.Max(d => d.LastTimestamp) - start) / (double)Stopwatch.Frequency : 0;
        int expected = settings.Count * settings.Subscribers;
        int missing = expected - delivered;
        bool accounted = deliveries.Length == settings.Subscribers && deliveries.All(d => d.Received == settings.Count - dropped);
        bool valid = error is null && sent == settings.Count && duplicates == 0 && invalid == 0 && failures == 0 && accounted && (settings.AllowDrops || missing == 0);
        if (!valid && error is null) error = "Delivery invariants failed; inspect missing, duplicate, dropped, and failure counts.";
        return new RunResult
        {
            Settings = settings, Runtime = RuntimeInformation.FrameworkDescription + " / " + RuntimeInformation.OSDescription + " / " + RuntimeInformation.ProcessArchitecture,
            MassTransitVersion = typeof(MassTransit.IBus).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            FoundatioVersion = typeof(IQueueClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            StartedUtc = startedUtc, Valid = valid, Error = error, Sent = sent, ExpectedDeliveries = expected, Delivered = delivered, Missing = missing,
            Duplicates = duplicates, InvalidMessages = invalid, Dropped = dropped, TransportPublished = counters.Published - publishedBefore, TransportFailures = failures,
            ApiSeconds = start > 0 && apiEnd > 0 ? (apiEnd - start) / (double)Stopwatch.Frequency : 0, DeliverySeconds = seconds,
            DrainSeconds = start > 0 && drained > 0 ? (drained - start) / (double)Stopwatch.Frequency : 0,
            MessagesPerSecond = seconds > 0 ? delivered / (double)settings.Subscribers / seconds : 0,
            DeliveriesPerSecond = seconds > 0 ? delivered / seconds : 0,
            ApiLatency = Distribution.From(apiLatency.Where(t => t > 0)), DeliveryLatency = Distribution.From(deliveries.SelectMany(d => d.LatencyTicks ?? [])),
            ScheduledDeliveryLatency = Distribution.From(deliveries.SelectMany(d => d.ScheduledLatencyTicks ?? [])),
            Processes = [producerCost!, .. settings.Broker ? deliveries.Select(d => d.Cost!).ToArray() : Array.Empty<ProcessCost>()],
            SubscriberDeliveries = deliveries.Select(d => d.Received).ToArray()
        };

        async Task WaitForDeliveryAsync(int expectedPerSubscriber, bool warmup, CancellationToken token)
        {
            while (true)
            {
                var states = new List<DeliverySnapshot>();
                long processed = counters.Processed;
                foreach (var child in children)
                {
                    var state = await child.RequestAsync(warmup ? "warmup" : "status", token);
                    states.AddRange(state.Deliveries);
                    processed += state.Processed;
                    if (state.Failures > 0) throw new InvalidOperationException("Consumer transport reported failures.");
                }
                states.AddRange(collectors.Select(c => c.Snapshot(warmup)));
                if (states.Any(s => s.Invalid > 0 || s.Duplicates > 0)) throw new InvalidOperationException("Invalid or duplicate deliveries detected.");
                bool settled = settings.Framework != "foundatio" || settings.Operation != "queue" || settings.Transport == "local" || processed >= expectedPerSubscriber + (warmup ? 0 : settings.Warmup);
                if (states.Count == settings.Subscribers && states.All(s => s.Received >= expectedPerSubscriber) && settled) return;
                await Task.Delay(20, token);
            }
        }
    }

    private static async Task SendAsync(Driver producer, Settings settings, bool warmup, string payload, long[]? apiLatency, Action accepted, CancellationToken ct, long start = 0)
    {
        int next = -1;
        int count = warmup ? settings.Warmup : settings.Count;
        await Task.WhenAll(Enumerable.Range(0, settings.Producers).Select(_ => Task.Run(async () =>
        {
            while (true)
            {
                int sequence = Interlocked.Increment(ref next);
                if (sequence >= count) return;
                long scheduled = !warmup && settings.Rate > 0 ? start + (long)(sequence * (double)Stopwatch.Frequency / settings.Rate) : 0;
                if (scheduled > 0)
                    while (Stopwatch.GetTimestamp() < scheduled)
                    {
                        ct.ThrowIfCancellationRequested();
                        double ms = (scheduled - Stopwatch.GetTimestamp()) * 1000d / Stopwatch.Frequency;
                        if (ms > 2) await Task.Delay(TimeSpan.FromMilliseconds(ms - 1), ct);
                        else Thread.SpinWait(16);
                    }
                long before = Stopwatch.GetTimestamp();
                await producer.SendAsync(sequence, warmup, before, scheduled > 0 ? scheduled : before, payload, ct);
                if (apiLatency is not null) apiLatency[sequence] = Stopwatch.GetTimestamp() - before;
                accepted();
            }
        }, ct)));
    }
}
