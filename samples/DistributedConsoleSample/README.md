# Distributed console sample

From the repository root, run `./build/setup-foundatio-core.ps1`, then `dotnet run --project samples/DistributedConsoleSample`.

No Docker is needed. The sample configures native Foundatio in-memory messaging and execution tracking, starts a host, rejects an invalid request before sending, enqueues a greeting, reports progress through `MessageProcessingContext`, and observes completion through `IJobMonitor`.

`EnqueueAsync` returns acceptance and a receipt. Waiting for completion is a separate operation. The same handler can run against native AWS messaging and Redis tracking without changing its method signature.
