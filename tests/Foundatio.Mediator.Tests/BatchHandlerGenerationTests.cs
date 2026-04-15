namespace Foundatio.Mediator.Tests;

public class BatchHandlerGenerationTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public async Task BatchHandler_IReadOnlyList()
    {
        var source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(DisableOpenTelemetry = true)]

            public record OrderCreated(int Id);

            public class OrderBatchHandler
            {
                public Task HandleAsync(IReadOnlyList<OrderCreated> events, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }

    [Fact]
    public async Task BatchHandler_Array()
    {
        var source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(DisableOpenTelemetry = true)]

            public record OrderCreated(int Id);

            public class OrderBatchHandler
            {
                public Task HandleAsync(OrderCreated[] events, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }

    [Fact]
    public async Task BatchHandler_WithDI()
    {
        var source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(DisableOpenTelemetry = true)]

            public record OrderCreated(int Id);
            public class OrderRepository { }

            public class OrderBatchHandler
            {
                public Task HandleAsync(IReadOnlyList<OrderCreated> events, OrderRepository repo, CancellationToken ct)
                    => Task.CompletedTask;
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }
}
