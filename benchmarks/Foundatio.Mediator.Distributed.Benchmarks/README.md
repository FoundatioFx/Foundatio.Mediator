# Native delivery comparison

This runner measures the same generated no-op handler in the PR 149 runtime and the native Foundatio alternative. It measures enqueue acceptance, handler entry, drain throughput, CPU, allocations, unique deliveries, and duplicates. Queue transport can be in memory or LocalStack SQS; tracking can use memory or Redis.

From the repository root, run `./build/setup-foundatio-core.ps1` first. Then:

```powershell
./benchmarks/Foundatio.Mediator.Distributed.Benchmarks/compare.ps1
./benchmarks/Foundatio.Mediator.Distributed.Benchmarks/compare.ps1 -Tracking
./benchmarks/Foundatio.Mediator.Distributed.Benchmarks/compare.ps1 -Aws -Messages 2000
$env:BENCHMARK_REDIS_CONNECTION_STRING = 'localhost:6379'
./benchmarks/Foundatio.Mediator.Distributed.Benchmarks/compare.ps1 -Redis
./benchmarks/Foundatio.Mediator.Distributed.Benchmarks/compare.ps1 -Aws -Redis
./benchmarks/Foundatio.Mediator.Distributed.Benchmarks/compare.ps1 -Concurrency 8 -Messages 100000
```

The script clones the exact PR 149 revision into `.dependencies`, compiles its standalone comparison program, and alternates process order across repetitions. It checks every run for unique completion and duplicates. Raw JSON is written to `BenchmarkDotNet.Artifacts/native-comparison`.

For `-Aws`, start an isolated LocalStack with SQS enabled and set `BENCHMARK_AWS_URL` if needed (default `http://127.0.0.1:14566`). Only loopback emulator endpoints are accepted. Runs create uniquely prefixed queues and remove them afterward. `-Tracking` uses in-memory execution stores; `-Redis` enables tracking in an isolated local Redis. Set `BENCHMARK_REDIS_CONNECTION_STRING` (default `localhost:6379`). Runs use unique job IDs and queue names; retained Redis history expires according to the store policy. Keep tracked runs below 100,000 messages so the default history limit retains the full run for completion accounting.

Defaults: Release, 10,000 messages, concurrency 64, 256-character payload, one 1,000-message warmup, three alternating repetitions. Timing includes all sends and broker drain; latency samples end at handler entry. Warmup and host startup/shutdown are outside the measured interval. Broker statistics are approximate on SQS. These are diagnostic measurements on a shared development machine, not production capacity guarantees. Historical PR 149 benchmark results elsewhere in this folder are not results of this implementation.

See [the comparison report](../../docs/guide/distributed-comparison.md) for measured results and architectural tradeoffs.
