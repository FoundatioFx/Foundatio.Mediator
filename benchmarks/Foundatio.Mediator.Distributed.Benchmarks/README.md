# Native delivery comparison

This runner measures the same generated no-op handler in the PR 149 runtime and the native Foundatio alternative. It measures enqueue acceptance, handler completion, drain throughput, CPU, allocations, unique deliveries, and duplicates. It has an in-memory path and a LocalStack SQS path.

From the repository root, run `./build/setup-foundatio-core.ps1` first. Then:

```powershell
./benchmarks/Foundatio.Mediator.Distributed.Benchmarks/compare.ps1
./benchmarks/Foundatio.Mediator.Distributed.Benchmarks/compare.ps1 -Tracking
./benchmarks/Foundatio.Mediator.Distributed.Benchmarks/compare.ps1 -Aws -Messages 2000
```

The script clones the exact PR 149 revision into `.dependencies`, compiles its standalone comparison program, and alternates process order across repetitions. It checks every run for unique completion and duplicates. Raw JSON is written to `BenchmarkDotNet.Artifacts/native-comparison`.

For `-Aws`, start an isolated LocalStack with SQS enabled and set `BENCHMARK_AWS_URL` if needed (default `http://127.0.0.1:14566`). Only loopback emulator endpoints are accepted. Runs create uniquely prefixed queues and remove them afterward. Tracking uses in-memory execution stores in both implementations to isolate dispatch overhead; the application sample uses Redis.

Defaults: Release, 10,000 messages, concurrency 64, 256-character payload, one 1,000-message warmup, three alternating repetitions. Timing includes all sends and broker drain; latency samples end at handler entry. Warmup and host startup/shutdown are outside the measured interval. Broker statistics are approximate on SQS. These are diagnostic measurements on a shared development machine, not production capacity guarantees. Historical PR 149 benchmark results elsewhere in this folder are not results of this implementation.

See [the comparison report](../../docs/guide/distributed-comparison.md) for measured results and architectural tradeoffs.
