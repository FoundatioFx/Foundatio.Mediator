# Performance

Foundatio Mediator achieves near-direct call performance through C# interceptors and source generators, eliminating runtime reflection.

## Benchmark Results

> 📊 **Last Updated:** 2025-12-22

### Commands

| Method | Mean | Allocated |
|:-------|-----:|----------:|
| Direct_Command | 12.75 ns | 0 B |
| Foundatio_Command | 145.71 ns | 200 B |
| MediatR_Command | 99.61 ns | 128 B |
| Wolverine_Command | 432.97 ns | 720 B |
| MassTransit_Command | 3,018.31 ns | 4168 B |

### Queries

| Method | Mean | Allocated |
|:-------|-----:|----------:|
| Direct_Query | 63.76 ns | 192 B |
| Foundatio_Query | 222.20 ns | 464 B |
| MediatR_Query | 164.55 ns | 320 B |
| Wolverine_Query | 805.66 ns | 1288 B |
| MassTransit_Query | 20,711.65 ns | 12531 B |

### Events (Publish)

| Method | Mean | Allocated |
|:-------|-----:|----------:|
| Direct_Event | 12.60 ns | 0 B |
| Foundatio_Publish | 341.15 ns | 648 B |
| MediatR_Publish | 132.12 ns | 288 B |
| Wolverine_Publish | 886.17 ns | 1201 B |
| MassTransit_Publish | 3,140.57 ns | 4320 B |

### Queries with Dependencies

| Method | Mean | Allocated |
|:-------|-----:|----------:|
| Direct_QueryWithDependencies | 85.11 ns | 264 B |
| Foundatio_QueryWithDependencies | 251.02 ns | 536 B |
| MediatR_QueryWithDependencies | 183.92 ns | 392 B |
| Wolverine_QueryWithDependencies | 848.58 ns | 1432 B |
| MassTransit_QueryWithDependencies | 20,913.58 ns | 12597 B |

## Running Benchmarks Locally

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
```