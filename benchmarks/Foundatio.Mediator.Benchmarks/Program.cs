using BenchmarkDotNet.Running;

namespace Foundatio.Mediator.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Running Foundatio.Mediator vs MediatR vs MassTransit benchmarks...");
        var summary = BenchmarkRunner.Run<CoreBenchmarks>();
    }
}
