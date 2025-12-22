# Foundatio.Mediator Benchmarks

This project contains performance benchmarks comparing Foundatio.Mediator against MediatR and MassTransit.

## Running the Benchmarks

### Full Comparison (Default)

Run all benchmarks comparing Foundatio.Mediator vs MediatR vs MassTransit:

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release
# or explicitly:
dotnet run -c Release -- all
```

### Foundatio-Only Mode

Run only Foundatio.Mediator benchmarks for tracking performance across code changes:

```bash
cd benchmarks/Foundatio.Mediator.Benchmarks
dotnet run -c Release -- foundatio
# or shorthand:
dotnet run -c Release -- f
```

This mode is faster to run and useful when iterating on library performance optimizations.
