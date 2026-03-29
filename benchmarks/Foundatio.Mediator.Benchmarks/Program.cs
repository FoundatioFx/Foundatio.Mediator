using BenchmarkDotNet.Running;
using RhoMicro.BdnLogging;

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
                BenchmarkRunner.Run<FoundatioBenchmarks>(SpotlightConfig.Instance, args: args.Length > 1 ? args[1..] : []);
                break;

            case "all":
            case "compare":
            default:
                Console.WriteLine("Running Foundatio.Mediator comparison benchmarks...");
                BenchmarkRunner.Run<CoreBenchmarks>(SpotlightConfig.Instance, args: args.Length > 1 ? args[1..] : []);
                break;
        }
    }
}
