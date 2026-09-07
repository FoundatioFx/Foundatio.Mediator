# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/local/pubsub/p128/s8/c10/f1/b5000000/r0/w0 | 5/5 | 7777900 (6738830–8241219) | 0.001 | 0 / 25000000 | 0 |
| foundatio/local/queue/p128/s8/c8/f1/b5000000/r0/w0 | 5/5 | 7123025 (6986293–7249812) | 0.001 | 0 / 25000000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b250000/r0/w0 | 5/5 | 243338 (215237–269417) | 939.593 | 0 / 1250000 | 0 |
| foundatio/memory/queue/p128/s8/c8/f1/b250000/r0/w0 | 5/5 | 192569 (186573–193361) | 581.010 | 0 / 1250000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b5000/r0/w0 | 5/5 | 1499 (1361–1570) | 3322.785 | 0 / 25000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b5000/r0/w0 | 5/5 | 1356 (1239–1431) | 1273.485 | 0 / 25000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f1/b250000/r0/w0 | 5/5 | 68587 (64723–70999) | 2273.374 | 0 / 1250000 | 0 |
| masstransit/memory/queue/p128/s8/c8/f1/b250000/r0/w0 | 5/5 | 72116 (69763–73214) | 2292.959 | 0 / 1250000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b5000/r0/w0 | 5/5 | 935 (906–1101) | 636.422 | 0 / 25000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b5000/r0/w0 | 5/5 | 1273 (1205–1334) | 1112.807 | 0 / 25000 | 0 |
