# Notification buffering comparison

This benchmark starts two hosts sharing an in-memory Foundatio transport. It counts deliveries in the remote handler, without adding a second observation buffer. Run from the repository root after `./build/setup-foundatio-core.ps1`:

```powershell
dotnet run --project benchmarks/Foundatio.Mediator.Notification.Benchmarks -c Release -p:GeneratePackageOnBuild=false -- paced
dotnet run --project benchmarks/Foundatio.Mediator.Notification.Benchmarks -c Release --no-build -- mixed 1000
dotnet run --project benchmarks/Foundatio.Mediator.Notification.Benchmarks -c Release --no-build -- mixed 10000
dotnet run --project benchmarks/Foundatio.Mediator.Notification.Benchmarks -c Release --no-build -- burst
dotnet run --project benchmarks/Foundatio.Mediator.Notification.Benchmarks -c Release --no-build -- local-off
dotnet run --project benchmarks/Foundatio.Mediator.Notification.Benchmarks -c Release --no-build -- local-on
```

`paced` waits for each group of 100 cluster events. `burst` sends 10,000 cluster events without waiting for remote delivery. `mixed` interleaves 100 local-only events after each cluster event. The second argument is subscription capacity (default 1,000). `local-off` and `local-on` measure one million local-only events with the bridge disabled or enabled.

Output includes publisher time, process-wide allocations during publication, unique sender/remote deliveries, and remote duplicates. Allocations in burst modes exclude work performed after the publisher finishes. These modes expose buffering behavior; best-effort broadcast can drop events under overload and is not a durable queue.
