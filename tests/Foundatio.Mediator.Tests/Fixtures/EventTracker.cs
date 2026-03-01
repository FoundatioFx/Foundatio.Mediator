namespace Foundatio.Mediator.Tests.Fixtures;

/// <summary>
/// Shared event/execution tracker used across integration tests.
/// Records execution steps in order for verification.
/// </summary>
public class EventTracker
{
    private readonly List<string> _events = [];

    public IReadOnlyList<string> Events => _events;

    public void Record(string step)
    {
        lock (_events)
            _events.Add(step);
    }

    public void Reset() => _events.Clear();
}
