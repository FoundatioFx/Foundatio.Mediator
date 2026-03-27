namespace Foundatio.Mediator.Tests;

public class MediatorInfoAnalyzerTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    private static readonly MediatorInfoAnalyzer Analyzer = new();

    // ── FMED017: Endpoint route info ─────────────────────────────────────

    [Fact]
    public async Task FMED017_ShowsRouteOnConventionalGetHandler()
    {
        var src = """
            using Foundatio.Mediator;

            public record GetTodo(string TodoId);

            public class TodoHandler
            {
                public string Handle(GetTodo msg) => "done";
            }
            """;

        var diagnostics = await RunAnalyzerAsync(src, Analyzer);
        var d = Assert.Single(diagnostics, d => d.Id == "FMED017");
        Assert.Contains("GET", d.GetMessage());
        Assert.Contains("/todos/{todoId}", d.GetMessage());
    }

    [Fact]
    public async Task FMED017_ShowsRouteOnConventionalPostHandler()
    {
        var src = """
            using Foundatio.Mediator;

            public record CreateTodo(string Title);

            public class TodoHandler
            {
                public string Handle(CreateTodo msg) => "done";
            }
            """;

        var diagnostics = await RunAnalyzerAsync(src, Analyzer);
        var d = Assert.Single(diagnostics, d => d.Id == "FMED017");
        Assert.Contains("POST", d.GetMessage());
        Assert.Contains("/todos", d.GetMessage());
    }

    [Fact]
    public async Task FMED017_ShowsRouteWithExplicitRoute()
    {
        var src = """
            using Foundatio.Mediator;

            public record Login(string Username, string Password);

            [HandlerEndpointGroup("Auth")]
            public class AuthHandler
            {
                [HandlerEndpoint(Route = "login")]
                public string Handle(Login msg) => "ok";
            }
            """;

        var diagnostics = await RunAnalyzerAsync(src, Analyzer);
        var d = Assert.Single(diagnostics, d => d.Id == "FMED017");
        Assert.Contains("POST", d.GetMessage());
        Assert.Contains("/api/auth/login", d.GetMessage());
    }

    [Fact]
    public async Task FMED017_ShowsRouteWithGroupPrefix()
    {
        var src = """
            using Foundatio.Mediator;

            public record DeleteTodo(string TodoId);

            [HandlerEndpointGroup("Todos")]
            public class TodoHandler
            {
                public string Handle(DeleteTodo msg) => "done";
            }
            """;

        var diagnostics = await RunAnalyzerAsync(src, Analyzer);
        var d = Assert.Single(diagnostics, d => d.Id == "FMED017");
        Assert.Contains("DELETE", d.GetMessage());
        Assert.Contains("/api/todos/{todoId}", d.GetMessage());
    }

    [Fact]
    public async Task FMED017_NoRouteOnExcludedEndpoint()
    {
        var src = """
            using Foundatio.Mediator;

            public record GetTodo(string TodoId);

            public class TodoHandler
            {
                [HandlerEndpoint(Exclude = true)]
                public string Handle(GetTodo msg) => "done";
            }
            """;

        var diagnostics = await RunAnalyzerAsync(src, Analyzer);
        Assert.DoesNotContain(diagnostics, d => d.Id == "FMED017");
    }

    [Fact]
    public async Task FMED017_NoRouteOnEventHandler()
    {
        var src = """
            using Foundatio.Mediator;

            public record OrderCreatedEvent(string OrderId);

            public class OrderEventConsumer
            {
                public void Consume(OrderCreatedEvent msg) { }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(src, Analyzer);
        Assert.DoesNotContain(diagnostics, d => d.Id == "FMED017");
    }

    [Fact]
    public async Task FMED017_ActionVerbRoute()
    {
        var src = """
            using Foundatio.Mediator;

            public record CompleteTodo(string TodoId);

            [HandlerEndpointGroup("Todos")]
            public class TodoHandler
            {
                public string Handle(CompleteTodo msg) => "done";
            }
            """;

        var diagnostics = await RunAnalyzerAsync(src, Analyzer);
        var d = Assert.Single(diagnostics, d => d.Id == "FMED017");
        Assert.Contains("POST", d.GetMessage());
        Assert.Contains("/complete", d.GetMessage());
    }

    // ── FMED018: Handler location info ───────────────────────────────────

    [Fact]
    public async Task FMED018_ShowsHandlerOnMessageType()
    {
        var src = """
            using Foundatio.Mediator;

            public record GetTodo(string TodoId);

            public class TodoHandler
            {
                public string Handle(GetTodo msg) => "done";
            }
            """;

        var diagnostics = await RunAnalyzerAsync(src, Analyzer);
        var d = Assert.Single(diagnostics, d => d.Id == "FMED018");
        Assert.Contains("TodoHandler", d.GetMessage());
        Assert.Contains("Handle", d.GetMessage());
    }

    [Fact]
    public async Task FMED018_NoHandlerInfoOnNonMessage()
    {
        var src = """
            using Foundatio.Mediator;

            public record NotAMessage(string Value);
            """;

        var diagnostics = await RunAnalyzerAsync(src, Analyzer);
        Assert.DoesNotContain(diagnostics, d => d.Id == "FMED018");
    }

    [Fact]
    public async Task FMED018_HandlerInfoHasAdditionalLocation()
    {
        var src = """
            using Foundatio.Mediator;

            public record GetTodo(string TodoId);

            public class TodoHandler
            {
                public string Handle(GetTodo msg) => "done";
            }
            """;

        var diagnostics = await RunAnalyzerAsync(src, Analyzer);
        var d = Assert.Single(diagnostics, d => d.Id == "FMED018");
        Assert.NotEmpty(d.AdditionalLocations);
    }

    [Fact]
    public async Task FMED018_SkipsHandlerAndMiddlewareClasses()
    {
        var src = """
            using Foundatio.Mediator;

            public record Msg;
            public class MsgHandler
            {
                public void Handle(Msg m) { }
            }
            public class LoggingMiddleware
            {
                public void Before(object m) { }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(src, Analyzer);
        // Should have FMED018 for Msg, but NOT for MsgHandler or LoggingMiddleware
        var info = diagnostics.Where(d => d.Id == "FMED018").ToList();
        Assert.Single(info);
        Assert.Contains("MsgHandler", info[0].GetMessage());
    }
}
