# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/local/pubsub/p128/s8/c10/f1/b5000000/r0/w0 | 5/5 | 7079373 (6738910–8294017) | 0.001 | 0 / 25000000 | 0 |
| foundatio/local/queue/p128/s8/c8/f1/b5000000/r0/w0 | 5/5 | 7123280 (6939594–7799886) | 0.001 | 0 / 25000000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b250000/r0/w0 | 5/5 | 188132 (179327–193366) | 1180.736 | 0 / 1250000 | 0 |
| foundatio/memory/queue/p128/s8/c8/f1/b250000/r0/w0 | 5/5 | 192234 (185393–197404) | 582.579 | 0 / 1250000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b5000/r0/w0 | 5/5 | 1398 (1209–1465) | 3557.470 | 0 / 25000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b5000/r0/w0 | 5/5 | 1319 (1266–1433) | 1176.260 | 0 / 25000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f1/b250000/r0/w0 | 5/5 | 69506 (67860–71594) | 2307.234 | 0 / 1250000 | 0 |
| masstransit/memory/queue/p128/s8/c8/f1/b250000/r0/w0 | 5/5 | 72412 (71038–73801) | 2291.082 | 0 / 1250000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b5000/r0/w0 | 5/5 | 975 (905–984) | 505.285 | 0 / 25000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b5000/r0/w0 | 5/5 | 1282 (1177–1289) | 1151.433 | 0 / 25000 | 0 |
