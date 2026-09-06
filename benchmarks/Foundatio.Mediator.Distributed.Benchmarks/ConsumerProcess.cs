using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace DistributedBenchmarks;

internal sealed record NodeSnapshot(DeliverySnapshot[] Deliveries, long Processed, int Failures);

internal sealed class ConsumerProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _errors;
    private ConsumerProcess(Process process)
    {
        _process = process;
        _errors = process.StandardError.ReadToEndAsync();
    }

    public static ProcessStartInfo StartInfo(params string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    public static async Task<ConsumerProcess> StartAsync(string settingsPath, int index, CancellationToken ct)
    {
        var child = new ConsumerProcess(Process.Start(StartInfo("node", settingsPath, index.ToString(System.Globalization.CultureInfo.InvariantCulture)))!);
        try
        {
            if (await child.ReadAsync(ct) != "ready") throw new InvalidOperationException("Consumer did not become ready.");
            return child;
        }
        catch { await child.DisposeAsync(); throw; }
    }

    private async Task<string> ReadAsync(CancellationToken ct)
    {
        string? line = await _process.StandardOutput.ReadLineAsync(ct);
        if (line is null) throw new InvalidOperationException("Consumer exited: " + await _errors.WaitAsync(ct));
        return line;
    }

    public async Task<NodeSnapshot> RequestAsync(string command, CancellationToken ct)
    {
        await _process.StandardInput.WriteLineAsync(command.AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
        return JsonSerializer.Deserialize<NodeSnapshot>(await ReadAsync(ct), Arguments.Json)!;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync("stop".AsMemory(), timeout.Token);
                await _process.StandardInput.FlushAsync(timeout.Token);
                await _process.WaitForExitAsync(timeout.Token);
            }
            if (_process.ExitCode != 0) Console.Error.WriteLine(await _errors);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        finally { _process.Dispose(); }
    }

    public static async Task RunNodeAsync(string path, int index)
    {
        var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(path), Arguments.Json)!;
        settings.Validate();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds * 3L));
        using var counters = new TransportCounters();
        await using var driver = new Driver(settings, consumer: true, index);
        await driver.StartAsync(lifetime.Token);
        Console.WriteLine("ready");
        while (await Console.In.ReadLineAsync(lifetime.Token) is { } command && command != "stop")
        {
            if (command == "begin") foreach (var collector in driver.Collectors) collector.Begin();
            var snapshot = new NodeSnapshot(driver.Collectors.Select(c => c.Snapshot(command == "warmup", command == "report")).ToArray(), counters.Processed, counters.Failures);
            Console.WriteLine(JsonSerializer.Serialize(snapshot, Arguments.Json));
        }
    }
}
