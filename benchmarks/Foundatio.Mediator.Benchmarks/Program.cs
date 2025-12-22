using BenchmarkDotNet.Running;

namespace Foundatio.Mediator.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";

        switch (mode)
        {
            case "foundatio":
            case "f":
                Console.WriteLine("Running Foundatio.Mediator-only benchmarks (for performance iteration)...");
                BenchmarkRunner.Run<FoundatioBenchmarks>(args: args.Length > 1 ? args[1..] : []);
                break;

            case "all":
            case "compare":
            default:
                Console.WriteLine("Running Foundatio.Mediator vs MediatR vs MassTransit benchmarks...");
                BenchmarkRunner.Run<CoreBenchmarks>(args: args.Length > 1 ? args[1..] : []);
                break;
        }
    }
}
