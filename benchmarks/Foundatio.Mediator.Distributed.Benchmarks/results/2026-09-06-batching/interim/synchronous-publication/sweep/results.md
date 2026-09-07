# Distributed benchmark results

Throughput is unique completed deliveries divided by fan-out and elapsed delivery time. Timing summaries use valid runs only; failures remain in counts and raw results. Min/max show run variation, not confidence intervals. Dropped runs measure overload and must not be ranked against lossless runs. See README for semantics and timing boundaries.

| Case | Valid runs | Median msg/s (min–max) | Median delivery p99 ms | Missing / offered deliveries | Drops |
|---|---:|---:|---:|---:|---:|
| foundatio/local/pubsub/p128/s8/c10/f1/b200000/r0/w0 | 3/3 | 6941463 (6861160–7519021) | 0.001 | 0 / 600000 | 0 |
| foundatio/local/queue/p128/s8/c8/f1/b200000/r0/w0 | 3/3 | 8195424 (5519261–11195813) | 0.001 | 0 / 600000 | 0 |
| foundatio/memory/pubsub/p128/s1/c10/f1/b50000/r0/w0 | 3/3 | 286615 (253815–332347) | 133.358 | 0 / 150000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b1000/r0/w0 | 3/3 | 196515 (172178–248182) | 4.185 | 11389 / 15000 | 11389 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b1000/r250/w0 | 3/3 | 250 (250–250) | 0.113 | 0 / 6000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f1/b50000/r0/w0 | 3/3 | 290189 (268476–300177) | 150.102 | 0 / 150000 | 0 |
| foundatio/memory/pubsub/p128/s8/c10/f3/b50000/r0/w0 | 3/3 | 163912 (162673–179539) | 275.150 | 0 / 450000 | 0 |
| foundatio/memory/pubsub/p4096/s8/c10/f1/b50000/r0/w0 | 3/3 | 107904 (91778–145764) | 441.712 | 0 / 150000 | 0 |
| foundatio/memory/queue/p128/s1/c8/f1/b50000/r0/w0 | 3/3 | 160891 (155599–191383) | 0.082 | 0 / 150000 | 0 |
| foundatio/memory/queue/p128/s8/c8/f1/b50000/r0/w0 | 3/3 | 184812 (184119–193936) | 135.358 | 0 / 150000 | 0 |
| foundatio/memory/queue/p128/s8/c8/f1/b50000/r0/w250 | 3/3 | 28278 (27960–28388) | 1646.174 | 0 / 150000 | 0 |
| foundatio/memory/queue/p4096/s8/c8/f1/b50000/r0/w0 | 3/3 | 111305 (109162–121304) | 179.540 | 0 / 150000 | 0 |
| foundatio/sqs/pubsub/p128/s1/c10/f1/b2000/r0/w0 | 3/3 | 1326 (1234–1345) | 1500.296 | 0 / 6000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b1000/r0/w0 | 3/3 | 1157 (862–1339) | 891.387 | 11880 / 15000 | 11880 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b1000/r250/w0 | 3/3 | 250 (250–250) | 55.377 | 0 / 6000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f1/b2000/r0/w0 | 3/3 | 1194 (1127–1213) | 1668.124 | 0 / 6000 | 0 |
| foundatio/sqs/pubsub/p128/s8/c10/f3/b2000/r0/w0 | 3/3 | 512 (498–550) | 3889.157 | 0 / 18000 | 0 |
| foundatio/sqs/pubsub/p4096/s8/c10/f1/b2000/r0/w0 | 3/3 | 977 (864–1090) | 2039.520 | 0 / 6000 | 0 |
| foundatio/sqs/queue/p128/s1/c8/f1/b2000/r0/w0 | 3/3 | 313 (293–315) | 9.057 | 0 / 6000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b2000/r0/w0 | 3/3 | 1176 (1137–1324) | 677.130 | 0 / 6000 | 0 |
| foundatio/sqs/queue/p128/s8/c8/f1/b2000/r0/w250 | 3/3 | 1033 (1028–1097) | 837.714 | 0 / 6000 | 0 |
| foundatio/sqs/queue/p4096/s8/c8/f1/b2000/r0/w0 | 3/3 | 972 (970–1229) | 732.319 | 0 / 6000 | 0 |
| masstransit/memory/pubsub/p128/s1/c10/f1/b50000/r0/w0 | 3/3 | 37983 (37452–39264) | 808.348 | 0 / 150000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f1/b1000/r250/w0 | 3/3 | 250 (250–250) | 0.237 | 0 / 6000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f1/b50000/r0/w0 | 3/3 | 39735 (39520–40111) | 1053.649 | 0 / 150000 | 0 |
| masstransit/memory/pubsub/p128/s8/c10/f3/b50000/r0/w0 | 3/3 | 35808 (34760–36283) | 1100.885 | 0 / 450000 | 0 |
| masstransit/memory/pubsub/p4096/s8/c10/f1/b50000/r0/w0 | 3/3 | 33372 (32023–35696) | 1124.668 | 0 / 150000 | 0 |
| masstransit/memory/queue/p128/s1/c8/f1/b50000/r0/w0 | 3/3 | 38935 (37750–39484) | 866.873 | 0 / 150000 | 0 |
| masstransit/memory/queue/p128/s8/c8/f1/b50000/r0/w0 | 3/3 | 40252 (40208–41244) | 1052.917 | 0 / 150000 | 0 |
| masstransit/memory/queue/p128/s8/c8/f1/b50000/r0/w250 | 3/3 | 3808 (3804–3816) | 12772.723 | 0 / 150000 | 0 |
| masstransit/memory/queue/p4096/s8/c8/f1/b50000/r0/w0 | 3/3 | 36374 (35341–36594) | 1085.728 | 0 / 150000 | 0 |
| masstransit/sqs/pubsub/p128/s1/c10/f1/b2000/r0/w0 | 3/3 | 220 (202–223) | 14.506 | 0 / 6000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b1000/r250/w0 | 3/3 | 250 (250–250) | 503.426 | 0 / 6000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f1/b2000/r0/w0 | 3/3 | 895 (889–983) | 202.067 | 0 / 6000 | 0 |
| masstransit/sqs/pubsub/p128/s8/c10/f3/b2000/r0/w0 | 3/3 | 414 (408–415) | 1475.611 | 0 / 18000 | 0 |
| masstransit/sqs/pubsub/p4096/s8/c10/f1/b2000/r0/w0 | 3/3 | 875 (864–891) | 379.826 | 0 / 6000 | 0 |
| masstransit/sqs/queue/p128/s1/c8/f1/b2000/r0/w0 | 3/3 | 312 (309–318) | 5.765 | 0 / 6000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b2000/r0/w0 | 3/3 | 1224 (1170–1233) | 553.938 | 0 / 6000 | 0 |
| masstransit/sqs/queue/p128/s8/c8/f1/b2000/r0/w250 | 3/3 | 1133 (987–1157) | 681.562 | 0 / 6000 | 0 |
| masstransit/sqs/queue/p4096/s8/c8/f1/b2000/r0/w0 | 3/3 | 1082 (917–1112) | 475.971 | 0 / 6000 | 0 |
