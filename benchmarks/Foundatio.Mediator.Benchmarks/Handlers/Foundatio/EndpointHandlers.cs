using Foundatio.Mediator.Benchmarks.Services;

namespace Foundatio.Mediator.Benchmarks.Handlers.Foundatio;

// ── Messages ────────────────────────────────────────────────────────────

public record GetBenchItem(string ItemId);

// ── Endpoint handler ────────────────────────────────────────────────────

[Handler]
public class BenchItemHandler
{
    [HandlerEndpoint]
    public Order Handle(GetBenchItem query)
        => new(42, 99.99m, DateTime.UtcNow);
}
