# Performance

Foundatio Mediator achieves near-direct call performance through C# interceptors and source generators, eliminating runtime reflection.

## Benchmark Results

> 📊 **Last Updated:** 2025-12-22
> 🔧 **Generated automatically by [GitHub Actions](https://github.com/FoundatioFx/Foundatio.Mediator/actions/workflows/benchmarks.yml)**

| Method | Mean | Allocated |
|:-------|-----:|----------:|
| Direct_Command | 7.143 ns | 0 B |
| Direct_Query | 30.135 ns | 192 B |
| Direct_Event | 5.898 ns | 0 B |
| Direct_QueryWithDependencies | 34.721 ns | 264 B |
| Foundatio_Command | 64.364 ns | 200 B |
| MediatR_Command | 38.909 ns | 128 B |
| MassTransit_Command | 1,249.631 ns | 4168 B |
| Foundatio_Query | 95.543 ns | 464 B |
| MediatR_Query | 63.362 ns | 320 B |
| MassTransit_Query | 5,583.887 ns | 12472 B |
| Foundatio_Publish | 144.341 ns | 648 B |
| MediatR_Publish | 49.039 ns | 288 B |
| MassTransit_Publish | 1,394.933 ns | 4320 B |
| Foundatio_QueryWithDependencies | 105.046 ns | 536 B |
| MediatR_QueryWithDependencies | 67.966 ns | 392 B |
| MassTransit_QueryWithDependencies | 5,642.623 ns | 12544 B |

## Running Benchmarks Locally

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
```