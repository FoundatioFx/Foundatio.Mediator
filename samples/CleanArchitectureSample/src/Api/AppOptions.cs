/// <summary>
/// Parsed command-line options for the Api host.
/// <code>
///   dotnet run                                        → full app (API + all workers)
///   dotnet run -- --mode api                          → API-only (no queue workers)
///   dotnet run -- --mode worker                       → worker-only (all queues)
///   dotnet run -- --mode worker --queues exports      → worker-only (specific queues)
/// </code>
/// </summary>
public sealed class AppOptions
{
    /// <summary>The running mode: "api", "worker", or "both" (default).</summary>
    public string Mode { get; private init; } = "both";

    /// <summary>When non-empty, only these queue groups will have workers started.</summary>
    public HashSet<string>? Queues { get; private init; }

    public bool IsApiEnabled => Mode is "api" or "both";
    public bool IsWorkerEnabled => Mode is "worker" or "both";

    public static AppOptions Parse(string[] args)
    {
        string mode = "both";
        HashSet<string>? queues = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--mode" && i + 1 < args.Length)
                mode = args[++i].ToLowerInvariant();
            else if (args[i] is "--queues" && i + 1 < args.Length)
                queues = args[++i]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return new AppOptions { Mode = mode, Queues = queues };
    }
}
