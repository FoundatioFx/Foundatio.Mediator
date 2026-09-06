using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using RhoMicro.BdnLogging;

namespace Foundatio.Mediator.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";

        IConfig config = Console.IsOutputRedirected ? DefaultConfig.Instance : SpotlightConfig.Instance;

        switch (mode)
        {
            case "foundatio":
            case "f":
                Console.WriteLine("Running Foundatio.Mediator-only benchmarks (for performance iteration)...");
                BenchmarkRunner.Run<FoundatioBenchmarks>(config, args: args.Length > 1 ? args[1..] : []);
                break;

            case "endpoints":
            case "e":
                Console.WriteLine("Running endpoint benchmarks (manual vs mediator)...");
                BenchmarkRunner.Run<EndpointBenchmarks>(args: args.Length > 1 ? args[1..] : []);
                break;

            case "all":
            case "compare":
            default:
                Console.WriteLine("Running Foundatio.Mediator comparison benchmarks...");
                BenchmarkRunner.Run<CoreBenchmarks>(config, args: args.Length > 1 ? args[1..] : []);
                break;
        }
    }
}
