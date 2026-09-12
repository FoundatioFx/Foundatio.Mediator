# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/sqs/pubsub/p128/s40/c10/f1/b5000/r0/w0 | 3/3 | 1344 (1270–1370) | 3704.171 | 0 / 15000 | 0 |
| masstransit/sqs/pubsub/p128/s40/c10/f1/b5000/r0/w0 | 3/3 | 1110 (1088–1295) | 2428.902 | 0 / 15000 | 0 |
