namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Which queue workers run in the current process. The same code runs everywhere; this one setting
/// decides whether a process is an enqueue-only API node, runs every worker, or runs a chosen set of
/// groups or queues so they can be scaled independently.
/// </summary>
/// <remarks>
/// Text form, for configuration and command lines: <c>all</c>, <c>none</c>, or a comma-separated list of
/// group or queue names. A leading <c>!</c> excludes a name; a list of only exclusions means "all but".
/// Names are matched case-insensitively against <see cref="QueueAttribute.Group"/> and the logical subscription name before resource prefixes.
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
        foreach (var name in included.Concat(excluded))
            ValidateName(name);
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

        var tokens = value.Split(',').Select(token => token.Trim()).ToArray();
        if (tokens.Length == 1 && string.Equals(tokens[0], "all", StringComparison.OrdinalIgnoreCase))
            return All;
        if (tokens.Length == 1 && string.Equals(tokens[0], "none", StringComparison.OrdinalIgnoreCase))
            return None;
        var included = new List<string>();
        var excluded = new List<string>();
        bool explicitAll = false;
        foreach (var token in tokens)
        {
            if (token.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                explicitAll = true;
                continue;
            }
            if (token.StartsWith('!'))
            {
                ValidateName(token[1..]);
                excluded.Add(token[1..]);
            }
            else
            {
                ValidateName(token);
                included.Add(token);
            }
        }
        if (explicitAll && included.Count > 0)
            throw new ArgumentException("'all' may only be combined with !name exclusions.", nameof(value));
        if (included.Intersect(excluded, StringComparer.OrdinalIgnoreCase).Any())
            throw new ArgumentException("A worker name cannot be both included and excluded.", nameof(value));

        return new WorkerSelection(included.Count == 0, included, excluded);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals("all", StringComparison.OrdinalIgnoreCase)
            || name.Equals("none", StringComparison.OrdinalIgnoreCase) || name[0] == '-'
            || name.Any(character => !char.IsLetterOrDigit(character) && character is not '_' and not '-' and not '.'))
            throw new ArgumentException($"Invalid worker name '{name}'. Use all, none, or comma-separated names with optional ! exclusions.");
    }

    internal void Validate(IEnumerable<string> availableNames)
    {
        var available = availableNames.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var unknown = _included.Concat(_excluded).Except(available, StringComparer.OrdinalIgnoreCase).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException($"Unknown worker selections: {string.Join(", ", unknown)}. Available subscriptions/groups: {string.Join(", ", available)}. Select logical names without ResourcePrefix.");
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
