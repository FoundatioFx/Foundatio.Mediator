# Performance

Foundatio Mediator achieves near-direct call performance through C# interceptors and source generators, eliminating runtime reflection.

## Benchmark Results

> 📊 **Last Updated:** 2025-12-21
> 🔧 **Generated automatically by [GitHub Actions](https://github.com/FoundatioFx/Foundatio.Mediator/actions/workflows/benchmarks.yml)**

| Method | Mean | Allocated |
|:-------|-----:|----------:|
| Direct_Command | 5.516 ns | 0 B |
| Direct_Query | 28.477 ns | 192 B |
| Direct_Event | 5.528 ns | 0 B |
| Direct_QueryWithDependencies | 32.654 ns | 264 B |
| Foundatio_Command | 62.583 ns | 200 B |
| MediatR_Command | 37.787 ns | 128 B |
| MassTransit_Command | 1,220.045 ns | 4168 B |
| Foundatio_Query | 94.020 ns | 464 B |
| MediatR_Query | 58.518 ns | 320 B |
| MassTransit_Query | 4,608.610 ns | 12472 B |
| Foundatio_Publish | 136.917 ns | 648 B |
| MediatR_Publish | 49.515 ns | 288 B |
| MassTransit_Publish | 1,313.789 ns | 4320 B |
| Foundatio_QueryWithDependencies | 96.809 ns | 536 B |
| MediatR_QueryWithDependencies | 64.964 ns | 392 B |
| MassTransit_QueryWithDependencies | 4,594.323 ns | 12544 B |

## Running Benchmarks Locally

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
```