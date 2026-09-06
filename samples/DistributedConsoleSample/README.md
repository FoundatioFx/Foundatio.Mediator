# Distributed console sample

Run from the repository root with .NET 10; Docker and Redis are unnecessary:

```powershell
dotnet run --project samples/DistributedConsoleSample
```

The program starts a host, rejects an invalid command before sending, enqueues valid work, prints its typed receipt, waits for delayed and active work to finish, and reads the completed job. `[Queue]` gives `GenerateGreetingHandler` its own subscription; general middleware runs during processing, and validation explicitly uses the enqueue stage.

The in-memory transport and state are for development/tests. Production can use `.UseAws()` and `.UseRedisJobState()` before or after `.AddDistributedQueues()`. Configure common prefixes and serialization with `.ConfigureDistributed()` before registering features.
