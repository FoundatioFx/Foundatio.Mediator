using BenchmarkDotNet.Running;

namespace Foundatio.Mediator.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Running Foundatio.Mediator vs MediatR benchmarks...");
        var summary = BenchmarkRunner.Run<SimpleBenchmarks>();
    }
}
