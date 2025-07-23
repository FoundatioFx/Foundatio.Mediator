using ConsoleSample.Messages;

namespace ConsoleSample.Handlers;

// Calculation handlers (sync and async examples)
public class SyncCalculationHandler
{
    public string Handle(SyncCalculationQuery query)
    {
        int result = query.A + query.B;
        Console.WriteLine($"🧮 Sync calculation: {query.A} + {query.B} = {result}");
        return $"Sum: {result}";
    }
}

public class AsyncCalculationHandler
{
    public async Task<string> HandleAsync(AsyncCalculationQuery query, CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken); // Simulate async work
        int result = query.A * query.B;
        Console.WriteLine($"🧮 Async calculation: {query.A} * {query.B} = {result}");
        return $"Product: {result}";
    }
}
