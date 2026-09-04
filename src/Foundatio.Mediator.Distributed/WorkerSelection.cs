namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Which queue workers run in the current process. The same code runs everywhere; this one setting
/// decides whether a process is an enqueue-only API node, runs every worker, or runs a chosen set of
/// groups or queues so they can be scaled independently.
/// </summary>
/// <remarks>
/// Text form, for configuration and command lines: <c>all</c>, <c>none</c>, or a comma-separated list of
/// group or queue names. A leading <c>!</c> excludes a name; a list of only exclusions means "all but".
/// Names are matched case-insensitively against <see cref="QueueAttribute.Group"/> and the fully prefixed queue name.
/// <code>
/// opts.Workers = WorkerSelection.Parse(builder.Configuration["Distributed:Workers"]); // e.g. "exports,imports"
/// opts.Workers = WorkerSelection.Only("exports");
/// opts.Workers = WorkerSelection.Except("imports");
/// opts.Workers = WorkerSelection.None; // API node
/// </code>
/// </remarks>
public sealed class WorkerSelection
{
    private readonly HashSet<string> _included;
    private readonly HashSet<string> _excluded;
    private readonly bool _all;

    private WorkerSelection(bool all, IEnumerable<string> included, IEnumerable<string> excluded)
    {
        _all = all;
        _included = new HashSet<string>(included, StringComparer.OrdinalIgnoreCase);
        _excluded = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every worker runs in this process. The default, and the right choice for a single-process deployment.
    /// </summary>
    public static WorkerSelection All { get; } = new(true, [], []);

    /// <summary>
    /// No workers run; the process only enqueues.
    /// </summary>
    public static WorkerSelection None { get; } = new(false, [], []);

    /// <summary>
    /// Only workers whose group or queue name is listed run.
    /// </summary>
    public static WorkerSelection Only(params string[] groupsOrQueues)
    {
        if (groupsOrQueues is null || groupsOrQueues.Length == 0)
            throw new ArgumentException("Specify at least one group or queue name.", nameof(groupsOrQueues));

        return new WorkerSelection(false, groupsOrQueues, []);
    }

    /// <summary>
    /// Every worker runs except those whose group or queue name is listed.
    /// </summary>
    public static WorkerSelection Except(params string[] groupsOrQueues)
    {
        if (groupsOrQueues is null || groupsOrQueues.Length == 0)
            throw new ArgumentException("Specify at least one group or queue name.", nameof(groupsOrQueues));

        return new WorkerSelection(true, [], groupsOrQueues);
    }

    /// <summary>
    /// Parses the text form. Null, empty, or <c>all</c> means <see cref="All"/>; <c>none</c> means <see cref="None"/>.
    /// </summary>
    public static WorkerSelection Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return All;

        var tokens = value!.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 1)
        {
            if (string.Equals(tokens[0], "all", StringComparison.OrdinalIgnoreCase) || tokens[0] == "*")
                return All;
            if (string.Equals(tokens[0], "none", StringComparison.OrdinalIgnoreCase))
                return None;
        }

        var included = new List<string>();
        var excluded = new List<string>();
        foreach (var token in tokens)
        {
            if (token.StartsWith('!') || token.StartsWith('-'))
            {
                if (token.Length > 1)
                    excluded.Add(token[1..]);
            }
            else if (!string.Equals(token, "all", StringComparison.OrdinalIgnoreCase) && token != "*")
            {
                included.Add(token);
            }
        }

        return new WorkerSelection(included.Count == 0, included, excluded);
    }

    /// <summary>
    /// Whether this process runs the worker for a queue.
    /// </summary>
    public bool Includes(string queueName, string? group)
    {
        if (_excluded.Contains(queueName) || (group is not null && _excluded.Contains(group)))
            return false;

        if (_all)
            return true;

        return _included.Contains(queueName) || (group is not null && _included.Contains(group));
    }

    /// <summary>
    /// Whether no worker at all runs in this process.
    /// </summary>
    public bool IsNone => !_all && _included.Count == 0;

    /// <summary>
    /// Whether every worker runs in this process.
    /// </summary>
    public bool IsAll => _all && _excluded.Count == 0;

    public override string ToString()
    {
        if (IsAll) return "all";
        if (IsNone) return "none";

        var parts = _included.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Concat(_excluded.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n => "!" + n));
        return string.Join(",", parts);
    }
}
