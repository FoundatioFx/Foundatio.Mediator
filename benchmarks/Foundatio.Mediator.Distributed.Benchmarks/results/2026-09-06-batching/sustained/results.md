# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/sqs/pubsub/p128/s8/c10/f1/b100000/r0/w0 | 1/1 | 1845 (1845–1845) | 53988.641 | 0 / 100000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b100000/r0/w0 | 3/3 | 2460 (2369–2471) | 2225.623 | 0 / 300000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b100000/r0/w0 | 1/1 | 1122 (1122–1122) | 10702.874 | 0 / 100000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b100000/r0/w0 | 3/3 | 1794 (1783–1834) | 15474.112 | 0 / 300000 | 0 |
