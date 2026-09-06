# Distributed queue and pub/sub benchmarks

This executable compares completed work across Foundatio's local mediator, distributed in-memory workers, and SQS/SNS, plus MassTransit using its in-memory and SQS/SNS transports. Broker-backed consumers run in separate .NET processes. The producer and consumers run on the same machine; the broker can be LocalStack or AWS.

The [September 6 measurements](results/2026-09-06-localstack/README.md) include a 30-run longer baseline, a 120-run scaling/overload sweep, raw results, and broker request-count analysis. They retain the failed fixed-rate case and shared-host limitations.

## Run it

From the repository root, in PowerShell:

```powershell
$project = 'benchmarks/Foundatio.Mediator.Distributed.Benchmarks'
dotnet build $project -c Release -p:GeneratePackageOnBuild=false
$runner = "$project/bin/Release/net10.0/Foundatio.Mediator.Distributed.Benchmarks.dll"
dotnet $runner self-test
docker compose -p mediator-bench -f "$project/compose.yaml" up -d --wait
dotnet $runner matrix --suite smoke --repetitions 1 --output ./BenchmarkDotNet.Artifacts/distributed/smoke
dotnet $runner matrix --suite sweep --count 5000 --repetitions 3 --timeout 300 --output ./BenchmarkDotNet.Artifacts/distributed/sweep
docker compose -p mediator-bench -f "$project/compose.yaml" down
```

Set `$env:BENCHMARK_PORT = '14566'` before Compose and pass `--endpoint http://localhost:14566` when port 4566 is occupied. Compose starts a dedicated, pinned LocalStack 3.8.1 container. Do not run competing builds, tests, profilers, or sample workers during a measurement. Record other host load and the Docker image digest with shared results.

The build command disables NuGet packing: this standalone benchmark build does not need the separate CodeFixes package output produced by a full solution build.

`smoke` sends 100 measured messages per case and checks functionality; its timings are not performance evidence. `standard` runs all ten framework/transport/operation combinations. `sweep` also measures 4 KiB payloads, one producer, three subscribers, 250 microseconds of handler work for queues, fixed-rate publishing at 250 messages/second, and notification overload with the default 1,000-entry buffer. Every matrix case gets a fresh process and unique resource prefix. Case order is shuffled for each repetition using `--seed` (default 149).

Standard/sweep local baselines send 2,000,000 messages and in-memory cases 250,000 to amortize short-run overhead; override these with `--local-count` and `--memory-count`. Broker and fixed-rate cases use `--count`. The explicit overload case uses at least 5,000. Settings are saved for every run. Use `run` for an exact count or a workload outside the presets:

```powershell
# Untracked durable queue, eight concurrent producers and consumers.
dotnet $runner run --framework foundatio --transport sqs --operation queue --count 10000 --producers 8 --concurrency 8
dotnet $runner run --framework masstransit --transport sqs --operation queue --count 10000 --producers 8 --concurrency 8

# Same payload and three remote subscribers; enough outbound buffer for the offered burst.
dotnet $runner run --framework foundatio --transport sqs --operation pubsub --count 10000 --fanout 3 --capacity 10000
dotnet $runner run --framework masstransit --transport sqs --operation pubsub --count 10000 --fanout 3 --capacity 10000

# Default-buffer saturation: report drops instead of treating dropped work as a speedup.
dotnet $runner run --transport sqs --operation pubsub --count 10000 --allow-drops

# Rate-driven latency includes time waiting for a producer to become available.
dotnet $runner run --transport sqs --operation queue --count 10000 --rate 500 --payload 4096 --work-us 250
```

Other options: `--warmup` (200 by default; local matrix uses 5,000), `--timeout` (180 seconds including startup/warmup), `--region`, `--output`, `--repetitions`. Payload size is the UTF-8 business payload string length (1–65,536 bytes), before JSON/envelopes/headers. All handlers check run id, sequence, and payload boundaries and record completion. `--work-us` adds the same CPU work in both frameworks. Source-generated local handlers and distributed handlers use the same collector; its overhead is included.

## What the numbers mean

| Measurement | Boundary |
|---|---|
| API latency | Immediately before enqueue/publish/invoke through its returned task completing; includes message-record allocation |
| Delivery latency | The same start timestamp through completion of the business handler, per unique delivery |
| Scheduled delivery latency | Intended fixed-rate arrival through handler completion, including late admission when all producers are busy |
| Messages/second | Unique completed deliveries / subscriber count / time from measurement start to final handler completion |
| Deliveries/second | Unique completed deliveries / that same elapsed time |
| Drain seconds | From measurement start until completion accounting and broker quiescence checks finish; includes polling overhead |
| CPU / allocations / GC | Process deltas after warmup through measurement/drain, reported for producer and each remote consumer; exclude the broker |

Percentiles use nearest rank over delivered messages, including p50, p95, p99, and max. Missing messages are never assigned a made-up latency. Both the offered denominator and each subscriber's completion count are recorded. Results contain duplicates, invalid messages, drops, transport failures, and validity. A missing, duplicate, invalid, or failed queue delivery fails the run. `--allow-drops` permits only missing notifications accounted for by Foundatio's actual drop counter, equally at every subscriber. Those overload results must be reported separately from lossless results.

Warmup has separate sequence tracking and completes before timing. Foundatio notification runs also wait for the actual transport-published counter plus the drop counter to account for every publication. For queues, Foundatio's processed metric confirms worker settlement. Both broker adapters require two consecutive zero-depth readings across this run's queues, including invisible/delayed messages, and reject nonempty error/dead-letter queues. SQS counts are approximate; this is a quiescence check, not an exactly-once or redelivery soak test. Handler completion is the common latency boundary; do not call it broker acknowledgment latency.

The monotonic clock is shared by processes on the same host. The harness does **not** support moving a consumer process to another machine and comparing unsynchronized timestamps. For `--rate 0`, the load is closed-loop with a fixed number of producers; it does not correct coordinated omission. Use a positive `--rate` and scheduled latency to see late admission. A run's offered rate can exceed sustainable throughput; compare actual throughput and scheduled tails as well as API latency.

## Comparison controls and differences

- Queue jobs are untracked. No Redis state, outbox, database, handler retries, or application work is added to one side alone. The existing [distributed load probe](../Foundatio.Mediator.Distributed.Load/README.md) separately measures in-memory/Redis job-state overhead and transport operation counts.
- Queue consumer concurrency and prefetch are both `--concurrency`. Pub/sub uses ten concurrent messages per endpoint on both sides, matching Foundatio's SNS/SQS batch receiver. Each fan-out subscriber has its own host/process and queue in broker runs. In-memory MassTransit uses endpoints on one bus; Foundatio uses separate hosts sharing its in-memory pub/sub client.
- Native framework serialization, envelopes, transport batching, and acknowledgment behavior stay enabled. Identical business payloads do **not** imply identical wire bytes or protocol cost. This compares the public application APIs with these settings, not raw broker limits.
- Foundatio notifications are best effort: publishing writes to a bounded drop-oldest buffer, and the outbound worker allows up to 40 concurrent transport publications by default. The AWS transport coalesces concurrent sends, acknowledgments, and SNS publications into batches, with a 1 ms partial-batch window and four concurrent batches per destination/operation. SNS filters redundant self-delivery at the broker. MassTransit bus publication awaits transport acceptance and supports durable consumer/error handling. These are different delivery guarantees. The benchmark retains each framework's native batching and scheduling; equal producer and consumer limits do not imply identical internal scheduling. Enlarging Foundatio's buffer in the lossless burst matrix retains the offered workload; it does not change the guarantee or add backpressure.
- Publisher-only Foundatio hosts set `ReceiveNotifications = false`, just as queue producers select no workers and MassTransit producers configure no business receive endpoint. Receiving hosts retain their subscriptions. MassTransit's temporary bus endpoint is included. Every business queue/topic and temporary bus queue gets an explicit run prefix. Historical measurements identify runs made before publisher-only configuration was available.
- Local dispatch is a useful application baseline; instrumentation and concurrent producer scheduling dominate its nanosecond-scale handler cost. Use the existing [BenchmarkDotNet suite](../Foundatio.Mediator.Benchmarks) for precise local dispatch overhead and regression comparisons.
- LocalStack emulates broker behavior and adds its own bottlenecks. Results on it establish harness correctness and relative behavior on that emulator/host. They do not establish AWS capacity, production cost, or a universal framework ranking. Repeat against AWS before making those claims.

The default comparison version is **MassTransit 8.5.10**. MassTransit 9.2.0 was tested during setup but requires a bus license. For an appropriately licensed run, rebuild with `-p:MassTransitVersion=9.2.0` and configure the license through MassTransit's supported environment settings. The executable reports the loaded version. See the official [SQS/SNS transport documentation](https://masstransit.massient.com/configuration/transports/amazon-sqs) and [license configuration](https://masstransit.massient.com/configuration/license).

## Real AWS and artifacts

Use `--aws --region us-east-1` to select the SDK's default AWS credential chain instead of LocalStack's test credentials. This creates temporary SQS queues, SNS topics, and subscriptions and incurs AWS charges. Use a dedicated benchmark account/role with permission to provision, send/receive, inspect, and delete those resources. The runner does not save credentials. Cleanup only deletes queues/topics whose names begin with the generated `fmb-<16 hex characters>-` prefix; it never purges account-wide resources. If the process is killed, the saved settings identify the prefix to clean up.

Each run writes settings and a machine-readable result. The result preserves partial measurements and an error on a controlled timeout/failure. `environment.json` records runtime, CPU count, GC mode, clock frequency, command, seed, case list, and the harness assembly hash. `runs.json` bundles the complete raw results for archiving. `results.csv` contains all repetitions; `results.md` reports medians and min/max variation, keeping failed runs visible. Regenerate the summaries from individual result files or an archived `runs.json` with:

```powershell
dotnet $runner report --output ./BenchmarkDotNet.Artifacts/distributed/sweep
```

Save the git SHA, CPU model/affinity, OS/runtime, broker version/digest, raw JSON/CSV, count, duration, and repeat count alongside any published comparison. Do not compare API throughput from the buffered publisher with completed-message throughput from another framework.
