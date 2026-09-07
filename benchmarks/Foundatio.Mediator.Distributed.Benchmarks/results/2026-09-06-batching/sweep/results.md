# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/local/pubsub/p128/s8/c10/f1/b200000/r0/w0 | 3/3 | 5761799 (5507552–12056366) | 0.001 | 0 / 600000 | 0 |
| foundatio/local/queue/p128/s8/c8/f1/b200000/r0/w0 | 3/3 | 5633720 (5463876–6446783) | 0.001 | 0 / 600000 | 0 |
| foundatio/memory/pubsub/p128/s1/c10/f1/b50000/r0/w0 | 3/3 | 323381 (226739–326542) | 129.429 | 0 / 150000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b1000/r0/w0 | 3/3 | 201604 (174369–251039) | 3.769 | 11329 / 15000 | 11329 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b1000/r250/w0 | 3/3 | 250 (250–250) | 0.128 | 0 / 6000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b50000/r0/w0 | 3/3 | 246370 (230271–274165) | 177.371 | 0 / 150000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f3/b50000/r0/w0 | 3/3 | 190817 (153858–193713) | 241.394 | 0 / 450000 | 0 |
| foundatio/memory/pubsub/p4096/s8/c10/f1/b50000/r0/w0 | 3/3 | 116300 (108944–119658) | 409.441 | 0 / 150000 | 0 |
| foundatio/memory/queue/p128/s1/c8/f1/b50000/r0/w0 | 3/3 | 155257 (142701–209039) | 0.368 | 0 / 150000 | 0 |
| foundatio/memory/queue/p128/s8/c8/f1/b50000/r0/w0 | 3/3 | 197936 (191041–206442) | 130.051 | 0 / 150000 | 0 |
| foundatio/memory/queue/p128/s8/c8/f1/b50000/r0/w250 | 3/3 | 28233 (28116–28648) | 1642.717 | 0 / 150000 | 0 |
| foundatio/memory/queue/p4096/s8/c8/f1/b50000/r0/w0 | 3/3 | 115572 (100778–116262) | 182.296 | 0 / 150000 | 0 |
| foundatio/sqs/pubsub/p128/s1/c10/f1/b2000/r0/w0 | 3/3 | 1400 (889–1519) | 1423.829 | 0 / 6000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b1000/r0/w0 | 3/3 | 1091 (989–1150) | 944.761 | 11880 / 15000 | 11880 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b1000/r250/w0 | 3/3 | 250 (250–250) | 57.878 | 0 / 6000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b2000/r0/w0 | 3/3 | 1143 (1125–1425) | 1742.961 | 0 / 6000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f3/b2000/r0/w0 | 3/3 | 494 (399–512) | 4030.111 | 0 / 18000 | 0 |
| foundatio/sqs/pubsub/p4096/s8/c10/f1/b2000/r0/w0 | 3/3 | 855 (728–1262) | 2329.212 | 0 / 6000 | 0 |
| foundatio/sqs/queue/p128/s1/c8/f1/b2000/r0/w0 | 3/3 | 346 (328–358) | 4.882 | 0 / 6000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b2000/r0/w0 | 3/3 | 1504 (1317–1636) | 299.972 | 0 / 6000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b2000/r0/w250 | 3/3 | 1411 (1134–1771) | 272.005 | 0 / 6000 | 0 |
| foundatio/sqs/queue/p4096/s8/c8/f1/b2000/r0/w0 | 3/3 | 1319 (1106–1587) | 74.658 | 0 / 6000 | 0 |
| masstransit/memory/pubsub/p128/s1/c10/f1/b50000/r0/w0 | 3/3 | 36670 (36333–38843) | 855.861 | 0 / 150000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f1/b1000/r250/w0 | 3/3 | 250 (250–250) | 0.285 | 0 / 6000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f1/b50000/r0/w0 | 3/3 | 38313 (34969–39154) | 1063.851 | 0 / 150000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f3/b50000/r0/w0 | 3/3 | 36875 (29074–37136) | 1076.599 | 0 / 450000 | 0 |
| masstransit/memory/pubsub/p4096/s8/c10/f1/b50000/r0/w0 | 3/3 | 34282 (33003–34665) | 1123.173 | 0 / 150000 | 0 |
| masstransit/memory/queue/p128/s1/c8/f1/b50000/r0/w0 | 3/3 | 39286 (39189–39666) | 838.371 | 0 / 150000 | 0 |
| masstransit/memory/queue/p128/s8/c8/f1/b50000/r0/w0 | 3/3 | 40729 (40479–41853) | 1037.048 | 0 / 150000 | 0 |
| masstransit/memory/queue/p128/s8/c8/f1/b50000/r0/w250 | 3/3 | 3809 (3808–3819) | 12794.456 | 0 / 150000 | 0 |
| masstransit/memory/queue/p4096/s8/c8/f1/b50000/r0/w0 | 3/3 | 36158 (35321–37128) | 1091.639 | 0 / 150000 | 0 |
| masstransit/sqs/pubsub/p128/s1/c10/f1/b2000/r0/w0 | 3/3 | 212 (194–223) | 15.349 | 0 / 6000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b1000/r250/w0 | 3/3 | 250 (249–250) | 332.659 | 0 / 6000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b2000/r0/w0 | 3/3 | 959 (913–962) | 252.458 | 0 / 6000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f3/b2000/r0/w0 | 3/3 | 410 (410–425) | 1533.399 | 0 / 18000 | 0 |
| masstransit/sqs/pubsub/p4096/s8/c10/f1/b2000/r0/w0 | 3/3 | 866 (776–889) | 349.832 | 0 / 6000 | 0 |
| masstransit/sqs/queue/p128/s1/c8/f1/b2000/r0/w0 | 3/3 | 311 (307–314) | 5.785 | 0 / 6000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b2000/r0/w0 | 3/3 | 1214 (1184–1306) | 531.820 | 0 / 6000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b2000/r0/w250 | 3/3 | 1188 (1142–1226) | 594.609 | 0 / 6000 | 0 |
| masstransit/sqs/queue/p4096/s8/c8/f1/b2000/r0/w0 | 3/3 | 1137 (1063–1142) | 365.306 | 0 / 6000 | 0 |
