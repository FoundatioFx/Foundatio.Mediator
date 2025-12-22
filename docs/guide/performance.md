# Performance

Foundatio Mediator achieves near-direct call performance through C# interceptors and source generators, eliminating runtime reflection.

## Benchmark Results

> 📊 **Last Updated:** 2025-12-22
> 🔧 **Generated automatically by [GitHub Actions](https://github.com/FoundatioFx/Foundatio.Mediator/actions/workflows/benchmarks.yml)**

| Method | Mean | Allocated |
|:-------|-----:|----------:|
| Direct_Command | 13.82 ns | 0 B |
| Direct_Query | 57.69 ns | 192 B |
| Direct_Event | 14.04 ns | 0 B |
| Direct_QueryWithDependencies | 75.99 ns | 264 B |
| Foundatio_Command | 158.02 ns | 200 B |
| MediatR_Command | 108.61 ns | 128 B |
| MassTransit_Command | 3,039.47 ns | 4168 B |
| Foundatio_Query | 213.05 ns | 464 B |
| MediatR_Query | 142.30 ns | 320 B |
| MassTransit_Query | 11,881.17 ns | 12472 B |
| Foundatio_Publish | 393.09 ns | 648 B |
| MediatR_Publish | 124.28 ns | 288 B |
| MassTransit_Publish | 3,223.86 ns | 4320 B |
| Foundatio_QueryWithDependencies | 225.26 ns | 536 B |
| MediatR_QueryWithDependencies | 159.80 ns | 392 B |
| MassTransit_QueryWithDependencies | 12,399.85 ns | 12545 B |

## Running Benchmarks Locally

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
```