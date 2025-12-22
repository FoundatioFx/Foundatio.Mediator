# Performance

Foundatio.Mediator aims to get as close to direct method call performance as possible while providing a full-featured mediator with excellent developer ergonomics. Through C# interceptors and source generators, we eliminate runtime reflection entirely.

## Benchmark Results

> 📊 **Last Updated:** 2025-12-22

### Commands

Fire-and-forget dispatch with no return value.

| Method                             |          Mean | Allocated |
|:-----------------------------------|:-------------:|----------:|
| Direct_Command                       |      5.536 ns |       0 B |
| MediatorNet_Command                  |      9.169 ns |       0 B |
| MediatR_Command                      |     40.458 ns |     128 B |
| Foundatio_Command                    |     60.987 ns |     200 B |
| Wolverine_Command                    |    171.784 ns |     704 B |
| MassTransit_Command                  |  1,213.024 ns |   4,184 B |

### Queries

Request/response dispatch returning an Order object.

| Method                             |          Mean | Allocated |
|:-----------------------------------|:-------------:|----------:|
| Direct_Query                         |     28.961 ns |     192 B |
| Direct_QueryWithDependencies         |     33.656 ns |     264 B |
| MediatorNet_Query                    |     33.510 ns |     120 B |
| MediatR_Query                        |     60.191 ns |     320 B |
| Foundatio_Query                      |     93.022 ns |     464 B |
| Wolverine_Query                      |    257.041 ns |   1,000 B |
| MassTransit_Query                    |  5,368.266 ns |  12,488 B |

### Events (Publish)

Notification dispatched to 2 handlers.

| Method                             |          Mean | Allocated |
|:-----------------------------------|:-------------:|----------:|
| Direct_Event                         |      5.689 ns |       0 B |
| MediatorNet_Publish                  |     10.769 ns |       0 B |
| MediatR_Publish                      |    101.197 ns |     792 B |
| Foundatio_Publish                    |    110.246 ns |     336 B |
| Wolverine_Publish                    |  1,829.026 ns |   2,840 B |
| MassTransit_Publish                  |  2,047.094 ns |   6,008 B |

### Full Query (Dependencies + Middleware)

Query where handler has an injected service (IOrderService) and timing middleware (Before/Finally or IPipelineBehavior).

| Method                             |          Mean | Allocated |
|:-----------------------------------|:-------------:|----------:|
| MediatorNet_FullQuery                |     41.487 ns |     192 B |
| MediatR_FullQuery                    |    139.304 ns |     744 B |
| Foundatio_FullQuery                  |    192.957 ns |     776 B |
| Wolverine_FullQuery                  |    262.386 ns |   1,000 B |
| MassTransit_FullQuery                |  5,578.827 ns |  12,560 B |

### Cascading Messages

CreateOrder returns an Order and publishes OrderCreatedEvent to 2 handlers. Foundatio uses tuple returns for automatic cascading; other libraries publish manually.

| Method                             |          Mean | Allocated |
|:-----------------------------------|:-------------:|----------:|
| MediatorNet_CascadingMessages        |     45.668 ns |     144 B |
| MediatR_CascadingMessages            |    173.084 ns |   1,168 B |
| Foundatio_CascadingMessages          |    116.241 ns |     568 B |
| Wolverine_CascadingMessages          |  2,355.686 ns |   4,064 B |
| MassTransit_CascadingMessages        |  8,922.815 ns |  18,746 B |

### Short-Circuit Middleware (Foundatio Only)

Middleware returns cached result; handler is never invoked. Useful for caching or authorization.

| Method                             |          Mean | Allocated |
|:-----------------------------------|:-------------:|----------:|
| Foundatio_ShortCircuit               |     65.948 ns |     368 B |

## Running Benchmarks Locally

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
```
