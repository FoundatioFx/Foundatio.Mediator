using Foundatio.Mediator.Models;

namespace Foundatio.Mediator.Utility;

/// <summary>
/// Provides topological sorting with support for relative ordering constraints (OrderBefore/OrderAfter)
/// and numeric order fallback. Uses Kahn's algorithm (BFS-based) for deterministic output.
/// </summary>
internal static class MiddlewareOrderingSorter
{
    /// <summary>
    /// Sorts items respecting OrderBefore/OrderAfter constraints with numeric Order as tiebreaker.
    /// </summary>
    /// <typeparam name="T">The type of items to sort.</typeparam>
    /// <param name="items">The items to sort.</param>
    /// <param name="getKey">Function to get the unique key (fully qualified type name) for an item.</param>
    /// <param name="getOrderBefore">Function to get the types this item must run before.</param>
    /// <param name="getOrderAfter">Function to get the types this item must run after.</param>
    /// <param name="getNumericOrder">Function to get the numeric order for tiebreaking.</param>
    /// <param name="cycleDiagnostics">Output list of diagnostic info for any cycles detected.</param>
    /// <param name="locationProvider">Optional function to get the location for diagnostics.</param>
    /// <returns>The sorted list of items.</returns>
    public static List<T> Sort<T>(
        List<T> items,
        Func<T, string> getKey,
        Func<T, IEnumerable<string>> getOrderBefore,
        Func<T, IEnumerable<string>> getOrderAfter,
        Func<T, int> getNumericOrder,
        out List<DiagnosticInfo> cycleDiagnostics,
        Func<T, LocationInfo?>? locationProvider = null)
    {
        cycleDiagnostics = [];

        if (items.Count <= 1)
            return [.. items];

        // Build a lookup from key to item + index
        var keyToItem = new Dictionary<string, T>(StringComparer.Ordinal);
        var keyToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < items.Count; i++)
        {
            var key = getKey(items[i]);
            keyToItem[key] = items[i];
            keyToIndex[key] = i;
        }

        // Check if any items have relative ordering constraints
        bool hasRelativeConstraints = false;
        foreach (var item in items)
        {
            if (getOrderBefore(item).Any() || getOrderAfter(item).Any())
            {
                hasRelativeConstraints = true;
                break;
            }
        }

        // Fast path: no relative constraints, just use numeric Order + secondary tiebreaker
        if (!hasRelativeConstraints)
            return [.. items];

        // Build adjacency list and in-degree map for Kahn's algorithm
        // Edge A → B means A must come before B
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var key = getKey(item);
            if (!adjacency.ContainsKey(key))
                adjacency[key] = [];
            if (!inDegree.ContainsKey(key))
                inDegree[key] = 0;
        }

        // Process OrderBefore: if A says OrderBefore = [B], then A must come before B → edge A → B
        foreach (var item in items)
        {
            var key = getKey(item);
            foreach (var beforeTarget in getOrderBefore(item))
            {
                // Only add edge if the target exists in our item set
                if (keyToIndex.ContainsKey(beforeTarget))
                {
                    adjacency[key].Add(beforeTarget);
                    inDegree[beforeTarget]++;
                }
            }
        }

        // Process OrderAfter: if A says OrderAfter = [C], then C must come before A → edge C → A
        foreach (var item in items)
        {
            var key = getKey(item);
            foreach (var afterTarget in getOrderAfter(item))
            {
                // Only add edge if the target exists in our item set
                if (keyToIndex.ContainsKey(afterTarget))
                {
                    if (!adjacency.ContainsKey(afterTarget))
                        adjacency[afterTarget] = [];
                    adjacency[afterTarget].Add(key);
                    inDegree[key]++;
                }
            }
        }

        // Kahn's algorithm with priority queue for deterministic tiebreaking
        // Items with in-degree 0 are ready; among those, prefer lower numeric Order
        var ready = new List<string>();
        foreach (var kvp in inDegree)
        {
            if (kvp.Value == 0)
                ready.Add(kvp.Key);
        }

        // Sort ready items by numeric order, then by name for stability
        ready.Sort((a, b) =>
        {
            int orderCmp = getNumericOrder(keyToItem[a]).CompareTo(getNumericOrder(keyToItem[b]));
            return orderCmp != 0 ? orderCmp : string.Compare(a, b, StringComparison.Ordinal);
        });

        var sorted = new List<T>(items.Count);
        var processedCount = 0;

        while (ready.Count > 0)
        {
            // Take the first ready item (lowest numeric order)
            var current = ready[0];
            ready.RemoveAt(0);

            sorted.Add(keyToItem[current]);
            processedCount++;

            // Collect newly ready items from this node's neighbors
            var newlyReady = new List<string>();
            foreach (var neighbor in adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    newlyReady.Add(neighbor);
            }

            // Sort newly ready items and merge them into the ready list maintaining sort order
            if (newlyReady.Count > 0)
            {
                newlyReady.Sort((a, b) =>
                {
                    int orderCmp = getNumericOrder(keyToItem[a]).CompareTo(getNumericOrder(keyToItem[b]));
                    return orderCmp != 0 ? orderCmp : string.Compare(a, b, StringComparison.Ordinal);
                });

                // Merge newlyReady into ready maintaining sorted order
                MergeSorted(ready, newlyReady, (a, b) =>
                {
                    int orderCmp = getNumericOrder(keyToItem[a]).CompareTo(getNumericOrder(keyToItem[b]));
                    return orderCmp != 0 ? orderCmp : string.Compare(a, b, StringComparison.Ordinal);
                });
            }
        }

        // Check for cycles: items with remaining in-degree > 0
        if (processedCount < items.Count)
        {
            var cycleParticipants = new List<string>();
            foreach (var kvp in inDegree)
            {
                if (kvp.Value > 0)
                    cycleParticipants.Add(kvp.Key);
            }

            // Emit a diagnostic warning for the cycle
            var participantNames = string.Join(", ", cycleParticipants.Select(p => p.Split('.').Last()));
            cycleDiagnostics.Add(new DiagnosticInfo
            {
                Identifier = "FMED012",
                Title = "Circular Ordering Dependency",
                Message = $"Circular ordering dependency detected between: {participantNames}. Falling back to numeric Order for these items.",
                Severity = DiagnosticSeverity.Warning,
                Location = locationProvider != null && cycleParticipants.Count > 0
                    ? locationProvider(keyToItem[cycleParticipants[0]])
                    : null
            });

            // Add cycle participants in numeric order as fallback
            var remaining = cycleParticipants
                .Select(k => keyToItem[k])
                .OrderBy(getNumericOrder)
                .ThenBy(i => getKey(i), StringComparer.Ordinal);

            sorted.AddRange(remaining);
        }

        return sorted;
    }

    /// <summary>
    /// Merges a sorted list of new items into an existing sorted list, maintaining sort order.
    /// </summary>
    private static void MergeSorted(List<string> target, List<string> source, Comparison<string> comparison)
    {
        if (source.Count == 0) return;

        int insertIndex = 0;
        foreach (var item in source)
        {
            // Find the correct insertion point
            while (insertIndex < target.Count && comparison(target[insertIndex], item) <= 0)
                insertIndex++;

            target.Insert(insertIndex, item);
            insertIndex++; // Move past the newly inserted item
        }
    }
}
