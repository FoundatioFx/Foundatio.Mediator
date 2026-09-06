using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DistributedBenchmarks;

try
{
    string command = args.FirstOrDefault() ?? "help";
    if (command == "node") { await ConsumerProcess.RunNodeAsync(args[1], int.Parse(args[2], CultureInfo.InvariantCulture)); return 0; }
    if (command == "case")
    {
        var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(args[1]), Arguments.Json)!;
        var result = await Scenario.RunAsync(settings, args[1]);
        Console.WriteLine(JsonSerializer.Serialize(result, Arguments.Json));
        return result.Valid ? 0 : 1;
    }
    if (command == "self-test") { SelfTests.Run(); return 0; }
    if (command is not ("run" or "matrix" or "report"))
    {
        Console.WriteLine("""
            Distributed mediator benchmarks
              run     --framework foundatio|masstransit --transport local|memory|sqs --operation queue|pubsub
              matrix  --suite smoke|standard|sweep --repetitions 3 --count 5000 --output ./results
              report  --output ./results
              self-test
            Workload: --payload 128 --producers 8 --concurrency 8 --fanout 1 --warmup 200
                      --capacity 1000 --rate 0 --work-us 0 --timeout 180 --allow-drops
            Transport: --endpoint http://localhost:4566 OR --aws --region us-east-1
            Pub/sub uses --concurrency 10. Larger fanout is supported for memory/SQS, not local.
            Burst pub/sub needs --capacity >= count for a lossless comparison; --allow-drops measures saturation.
            --aws creates temporary AWS resources and incurs AWS charges. Only this run's resources are deleted.
            """);
        return 0;
    }
    var values = Arguments.Parse(args[1..]);
    string output = Path.GetFullPath(values.GetValueOrDefault("output", Path.Combine("BenchmarkDotNet.Artifacts", "distributed", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture))));
    Directory.CreateDirectory(output);
    if (command == "report") { await Reports.WriteAsync(output); return 0; }
    var baseline = Settings.Parse(args[1..]);
    int localCount = int.Parse(values.GetValueOrDefault("local-count", "2000000"), CultureInfo.InvariantCulture);
    int memoryCount = int.Parse(values.GetValueOrDefault("memory-count", "250000"), CultureInfo.InvariantCulture);
    var cases = command == "run" ? new[] { baseline } : Matrix.Create(baseline, values.GetValueOrDefault("suite", "standard"), localCount, memoryCount).ToArray();
    int repetitions = int.Parse(values.GetValueOrDefault("repetitions", command == "run" ? "1" : "3"), CultureInfo.InvariantCulture);
    if (repetitions < 1 || repetitions > 100) throw new ArgumentOutOfRangeException(nameof(repetitions));
    int seed = int.Parse(values.GetValueOrDefault("seed", "149"), CultureInfo.InvariantCulture);
    await File.WriteAllTextAsync(Path.Combine(output, "environment.json"), JsonSerializer.Serialize(new
    {
        CapturedUtc = DateTimeOffset.UtcNow, Environment.MachineName, Environment.ProcessorCount,
        OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription, Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        Stopwatch.Frequency, ServerGc = System.Runtime.GCSettings.IsServerGC, Seed = seed, Repetitions = repetitions,
        Cases = cases.Select(c => c.Label), Command = args,
        HarnessSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(System.Reflection.Assembly.GetExecutingAssembly().Location)))
    }, new JsonSerializerOptions(Arguments.Json) { WriteIndented = true }));
    var random = new Random(seed);
    bool failed = false;
    for (int repetition = 0; repetition < repetitions; repetition++)
    {
        var order = cases.ToArray();
        random.Shuffle(order);
        foreach (var prototype in order)
        {
            var settings = prototype with { RunId = new Settings().RunId };
            settings.Validate();
            string stem = $"{repetition + 1:D2}-{settings.RunId}";
            string path = Path.Combine(output, stem + ".settings.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(settings, Arguments.Json));
            Console.WriteLine($"[{repetition + 1}/{repetitions}] {settings.Label} ({settings.Count:N0} messages)");
            using var process = Process.Start(ConsumerProcess.StartInfo("case", path))!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds + 120));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(entireProcessTree: true); throw; }
            string json = await stdout;
            await File.WriteAllTextAsync(Path.Combine(output, stem + ".result.json"), json);
            string errors = await stderr;
            if (!string.IsNullOrWhiteSpace(errors)) await File.WriteAllTextAsync(Path.Combine(output, stem + ".stderr.log"), errors);
            var result = JsonSerializer.Deserialize<RunResult>(json, Arguments.Json) ?? throw new InvalidOperationException("Case returned no result: " + errors);
            Console.WriteLine($"  {(result.Valid ? "PASS" : "FAIL")} {result.MessagesPerSecond:N0} msg/s; p99 {result.DeliveryLatency.P99Ms:N2} ms; delivered {result.Delivered:N0}/{result.ExpectedDeliveries:N0}; drops {result.Dropped:N0}");
            if (!result.Valid) Console.Error.WriteLine(result.Error);
            failed |= !result.Valid || process.ExitCode != 0;
        }
    }
    await Reports.WriteAsync(output);
    Console.WriteLine("Results: " + output);
    return failed ? 1 : 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

internal static class Matrix
{
    public static IEnumerable<Settings> Create(Settings baseline, string suite, int localCount, int memoryCount)
    {
        if (suite is not ("smoke" or "standard" or "sweep")) throw new ArgumentException("Use --suite smoke|standard|sweep.");
        foreach (string operation in new[] { "queue", "pubsub" })
            foreach (var (framework, transport) in new[] { ("foundatio", "local"), ("foundatio", "memory"), ("masstransit", "memory"), ("foundatio", "sqs"), ("masstransit", "sqs") })
            {
                int count = suite == "smoke" ? 100 : transport == "local" ? localCount : transport == "memory" ? memoryCount : baseline.Count;
                var settings = baseline with
                {
                    Framework = framework, Transport = transport, Operation = operation, Count = count,
                    Warmup = suite == "smoke" ? 10 : transport == "local" ? 5000 : baseline.Warmup,
                    Fanout = 1, Concurrency = operation == "pubsub" ? 10 : baseline.Concurrency,
                    Capacity = Math.Max(count, baseline.Warmup), AllowDrops = false
                };
                yield return settings;
                if (suite != "sweep" || transport == "local") continue;
                yield return settings with { PayloadBytes = 4096 };
                yield return settings with { Producers = 1 };
                if (operation == "pubsub") yield return settings with { Fanout = 3 };
                else yield return settings with { WorkMicroseconds = 250 };
            }
        if (suite == "sweep")
            foreach (string transport in new[] { "memory", "sqs" })
            {
                yield return baseline with { Framework = "foundatio", Transport = transport, Operation = "pubsub", Count = Math.Max(5000, baseline.Count), Concurrency = 10, Capacity = 1000, AllowDrops = true };
                foreach (string framework in new[] { "foundatio", "masstransit" })
                    yield return baseline with { Framework = framework, Transport = transport, Operation = "pubsub", Concurrency = 10, Rate = 250, Capacity = 1000, AllowDrops = false };
            }
    }
}

internal static class Reports
{
    public static async Task WriteAsync(string directory)
    {
        var results = new List<RunResult>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.result.json"))
            results.Add(JsonSerializer.Deserialize<RunResult>(await File.ReadAllTextAsync(path), Arguments.Json)!);
        string bundle = Path.Combine(directory, "runs.json");
        if (results.Count == 0 && File.Exists(bundle))
            results.AddRange(JsonSerializer.Deserialize<RunResult[]>(await File.ReadAllTextAsync(bundle), Arguments.Json)!);
        if (results.Count == 0) throw new ArgumentException("No benchmark results were found in " + directory);
        await File.WriteAllTextAsync(bundle, JsonSerializer.Serialize(results, Arguments.Json));
        var csv = new StringBuilder("case,valid,sent,delivered,missing,duplicates,dropped,msg_per_sec,delivery_p50_ms,delivery_p95_ms,delivery_p99_ms,api_p99_ms,scheduled_p99_ms,allocated_bytes,cpu_ms\n");
        foreach (var result in results.OrderBy(r => r.Settings.Label))
            csv.AppendLine(FormattableString.Invariant($"{result.Settings.Label},{result.Valid},{result.Sent},{result.Delivered},{result.Missing},{result.Duplicates},{result.Dropped},{result.MessagesPerSecond:F3},{result.DeliveryLatency.P50Ms:F6},{result.DeliveryLatency.P95Ms:F6},{result.DeliveryLatency.P99Ms:F6},{result.ApiLatency.P99Ms:F6},{result.ScheduledDeliveryLatency.P99Ms:F6},{result.Processes.Sum(p => p.AllocatedBytes)},{result.Processes.Sum(p => p.CpuMilliseconds):F3}"));
        await File.WriteAllTextAsync(Path.Combine(directory, "results.csv"), csv.ToString());
        var markdown = new StringBuilder("# Distributed benchmark results\n\nThroughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.\n\n| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |\n|---|---:|---:|---:|---:|---:|\n");
        foreach (var group in results.GroupBy(r => r.Settings.Label).OrderBy(g => g.Key))
        {
            var valid = group.Where(r => r.Valid).ToArray();
            string throughput = valid.Length > 0 ? FormattableString.Invariant($"{Median(valid.Select(r => r.MessagesPerSecond)):F0} ({valid.Min(r => r.MessagesPerSecond):F0}–{valid.Max(r => r.MessagesPerSecond):F0})") : "n/a";
            string p99 = valid.Length > 0 ? Median(valid.Select(r => r.DeliveryLatency.P99Ms)).ToString("F3", CultureInfo.InvariantCulture) : "n/a";
            markdown.AppendLine($"| {group.Key} | {valid.Length}/{group.Count()} | {throughput} | {p99} | {group.Sum(r => r.Missing)} / {group.Sum(r => r.ExpectedDeliveries)} | {group.Sum(r => r.Dropped)} |");
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "results.md"), markdown.ToString());
    }
    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 0 ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2 : sorted[sorted.Length / 2];
    }
}

internal static class SelfTests
{
    public static void Run()
    {
        var settings = new Settings { Count = 3, PayloadBytes = 1 };
        var collector = new DeliveryCollector(settings);
        long now = Stopwatch.GetTimestamp();
        var message = new BenchmarkMessage(settings.RunId, 1, false, now, now, "x");
        collector.Complete(message);
        collector.Complete(message);
        collector.Complete(message with { Sequence = 3 });
        collector.Complete(message with { Warmup = true });
        var snapshot = collector.Snapshot(false, true);
        if (snapshot.Received != 1 || snapshot.Duplicates != 1 || snapshot.Invalid != 1 || snapshot.LatencyTicks?.Length != 1 || collector.Snapshot(true).Received != 1)
            throw new InvalidOperationException("Delivery accounting or warmup isolation failed.");
        var distribution = Distribution.From(Enumerable.Range(1, 100).Select(i => (long)i * Stopwatch.Frequency));
        if (distribution.P50Ms != 50_000 || distribution.P95Ms != 95_000 || distribution.P99Ms != 99_000 || distribution.MaxMs != 100_000)
            throw new InvalidOperationException("Nearest-rank percentile calculation failed.");
        try { Settings.Parse(["--payload", "0"]); throw new Exception("Invalid settings accepted."); }
        catch (ArgumentOutOfRangeException) { }
        try { Settings.Parse(["--typo", "1"]); throw new Exception("Unknown option accepted."); }
        catch (ArgumentException) { }
        Console.WriteLine("PASS: unique/duplicate/invalid delivery accounting, phase isolation, percentiles, option validation.");
    }
}
