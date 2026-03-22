using Foundatio.Mediator.Benchmarks.Services;

namespace Foundatio.Mediator.Benchmarks.Handlers.Foundatio;

// ── Messages ────────────────────────────────────────────────────────────

public record GetBenchItem(string ItemId);
public record GetBenchWidget(string ItemId);
public record GetBenchWidgetV2(string ItemId);

// ── Unversioned endpoint handler ────────────────────────────────────────

[Handler]
[HandlerEndpointGroup("BenchItems")]
public class BenchItemHandler
{
    [HandlerAllowAnonymous]
    [HandlerEndpoint]
    public Order Handle(GetBenchItem query)
        => new(42, 99.99m, DateTime.UtcNow);
}

// ── Versioned endpoint handler (v1 — unversioned fallback) ─────────────

[Handler]
[HandlerEndpointGroup("BenchWidgets")]
public class BenchWidgetHandler
{
    [HandlerAllowAnonymous]
    [HandlerEndpoint]
    public Order Handle(GetBenchWidget query)
        => new(42, 99.99m, DateTime.UtcNow);
}

// ── Versioned endpoint handler (v2) ────────────────────────────────────

[Handler]
[HandlerEndpointGroup("BenchWidgets", ApiVersion = "2")]
public class BenchWidgetV2Handler
{
    [HandlerAllowAnonymous]
    [HandlerEndpoint(Route = "{itemId}")]
    public Order Handle(GetBenchWidgetV2 query)
        => new(42, 99.99m, DateTime.UtcNow);
}
