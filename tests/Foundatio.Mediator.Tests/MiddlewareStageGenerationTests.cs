namespace Foundatio.Mediator.Tests;

public class MiddlewareStageGenerationTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void NonqueuedHandler_StageMetadataPreservesItsNormalPipeline()
    {
        var result = RunGenerator("""
            using Foundatio.Mediator;
            public record Ping;
            public class PingHandler { public Result Handle(Ping message) => Result.Ok(); }
            [Middleware(Stage = MiddlewareStage.Enqueue)]
            public class ValidationMiddleware { public void Before(Ping message) { } }
            """, [new MediatorGenerator()]);
        var handler = Assert.Single(result.GeneratedTrees, tree => tree.HintName.StartsWith("PingHandler_"));
        Assert.Contains("validationMiddleware.Before(message)", handler.Source);
        Assert.DoesNotContain("HandlerDispatchContext", handler.Source);
    }

    [Fact]
    public void DispatcherWithoutAsyncExecute_ReportsActionableDiagnostic()
    {
        var result = RunGenerator("""
            using Foundatio.Mediator;
            public record Ping;
            public class PingHandler { public Result Handle(Ping message) => Result.Ok(); }
            [Middleware(IsDispatcher = true)]
            public class BrokenMiddleware { public void Before(Ping message) { } }
            """, [new MediatorGenerator()], assertCleanCompilation: false);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FMED019" && diagnostic.GetMessage().Contains("asynchronous Execute"));
    }
}
