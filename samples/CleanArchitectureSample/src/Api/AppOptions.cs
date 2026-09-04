/// <summary>
/// Parsed command-line options for the Api host.
/// <code>
///   dotnet run                                          → API + every worker in one process
///   dotnet run -- --mode api                            → API only; enqueues but runs no workers
///   dotnet run -- --mode worker                         → every worker, no HTTP
///   dotnet run -- --mode worker --workers exports       → only the "exports" group (or a queue name)
///   dotnet run -- --workers !imports                    → API + every worker except the "imports" group
/// </code>
/// The same selection can come from configuration as <c>Distributed:Workers</c> (env <c>Distributed__Workers</c>).
/// </summary>
public sealed class AppOptions
{
    /// <summary>The running mode: "api", "worker", or "both" (default).</summary>
    public string Mode { get; private init; } = "both";

    /// <summary>
    /// Worker selection text for <see cref="Foundatio.Mediator.Distributed.WorkerSelection.Parse"/>, or
    /// <c>null</c> to fall back to configuration. API-only mode forces <c>none</c>.
    /// </summary>
    public string? Workers { get; private init; }

    public bool IsApiEnabled => Mode is "api" or "both";

    public static AppOptions Parse(string[] args)
    {
        string mode = "both";
        string? workers = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--mode" && i + 1 < args.Length)
                mode = args[++i].ToLowerInvariant();
            else if (args[i] is "--workers" or "--queues" && i + 1 < args.Length)
                workers = args[++i];
        }

        if (mode is not ("api" or "worker" or "both"))
            throw new ArgumentException($"Unknown --mode '{mode}'. Use api, worker, or both.");

        if (mode == "api")
            workers = "none";
        else if (mode == "worker" && string.IsNullOrWhiteSpace(workers))
            workers = "all";

        return new AppOptions { Mode = mode, Workers = workers };
    }
}
