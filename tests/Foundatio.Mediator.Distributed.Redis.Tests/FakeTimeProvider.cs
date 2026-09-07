namespace Foundatio.Mediator.Distributed.Redis.Tests;

/// <summary>
/// Minimal controllable clock for tests that need deterministic timestamps and hour buckets.
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
