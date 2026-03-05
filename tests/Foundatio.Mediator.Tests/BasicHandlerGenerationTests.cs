using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Tests;

/// <summary>
/// Snapshot tests for generated handler code. Each test covers a distinct code generation
/// path so we can detect and approve changes in generated output. Keep this set small and
/// focused on structurally different output rather than minor variations.
/// </summary>
public class BasicHandlerGenerationTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    /// <summary>
    /// Default lifetime, no DI, OTel enabled (the default).
    /// Exercises: GetOrCreateHandler caching pattern, Activity try-catch-finally,
    /// both typed HandleAsync and UntypedHandleAsync generation.
    /// </summary>
    [Fact]
    public async Task DefaultStaticHandler_WithOTel()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Ping(string Message);

            public class PingHandler
            {
                public string Handle(Ping message) => message.Message + " Pong";
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }

    /// <summary>
    /// Scoped lifetime with constructor DI, OTel disabled.
    /// Exercises: GetRequiredService DI resolution, no Activity/try-catch,
    /// async handler with CancellationToken, pass-through generation.
    /// </summary>
    [Fact]
    public async Task ScopedDIHandler_NoOTel()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(DisableOpenTelemetry = true)]

            public record GetUser(int Id);
            public class UserService { public string FindUser(int id) => $"User-{id}"; }

            [Handler(Lifetime = MediatorLifetime.Scoped)]
            public class GetUserHandler(UserService users)
            {
                public Task<string> HandleAsync(GetUser query, CancellationToken ct)
                    => Task.FromResult(users.FindUser(query.Id));
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }

    /// <summary>
    /// Handler with Before/After/Finally middleware, OTel disabled.
    /// Exercises: middleware instance creation, Before state passing to After/Finally,
    /// try-catch-finally pipeline wrapping, middleware parameter resolution.
    /// </summary>
    [Fact]
    public async Task HandlerWithMiddlewarePipeline()
    {
        var source = """
            using System;
            using System.Diagnostics;
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(DisableOpenTelemetry = true)]

            public record ProcessOrder(int Id);

            public class ProcessOrderHandler
            {
                public string Handle(ProcessOrder cmd) => $"Processed-{cmd.Id}";
            }

            [Middleware(Order = 10)]
            public class TimingMiddleware
            {
                public Stopwatch Before(object message) => Stopwatch.StartNew();
                public void After(object message, Stopwatch sw) => sw.Stop();
                public void Finally(object message, Stopwatch sw, Exception? ex) => sw.Stop();
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }

    /// <summary>
    /// Middleware with a state-returning Before but no Finally method.
    /// Exercises: BeforeRan variable should NOT be emitted when Finally is absent (CS0219).
    /// </summary>
    [Fact]
    public async Task MiddlewareWithBeforeStateButNoFinally()
    {
        var source = """
            using System;
            using System.Diagnostics;
            using Foundatio.Mediator;

            public record ProcessOrder(string Id);

            public class ProcessOrderHandler
            {
                public string Handle(ProcessOrder cmd) => $"Processed-{cmd.Id}";
            }

            [Middleware(Order = 10)]
            public class LoggingMiddleware
            {
                public Stopwatch Before(object message) => Stopwatch.StartNew();
                public void After(object message, Stopwatch sw) => sw.Stop();
            }
            """;

        // AssertNoCompilationDiagnostics will catch CS0219 if BeforeRan is emitted but never read
        await VerifyGenerated(source, new MediatorGenerator());
    }

    /// <summary>
    /// Interceptor generation with a call site.
    /// Exercises: [InterceptsLocation] attribute emission, static dispatch method wiring.
    /// </summary>
    [Fact]
    public async Task InterceptorGeneration()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(DisableOpenTelemetry = true)]

            public record Echo(string Value);

            public class EchoHandler
            {
                public string Handle(Echo msg) => msg.Value;
            }

            public class Caller
            {
                public async Task<string> Run(IMediator mediator)
                {
                    return await mediator.InvokeAsync<string>(new Echo("hello"));
                }
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }

    /// <summary>
    /// Endpoint generation for a handler with [Endpoint] attribute.
    /// Exercises: _MediatorEndpoints.g.cs generation, route mapping, HTTP method,
    /// request binding, result mapping to IResult.
    /// </summary>
    [Fact]
    public async Task EndpointGeneration()
    {
        var refs = GetAspNetCoreReferences();

        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                DisableOpenTelemetry = true,
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetWidget(string Id);

            [Endpoint(Method = "GET", RouteTemplate = "/widgets/{Id}")]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => $"Widget-{query.Id}";
            }
            """;

        await VerifyGenerated(source, null, refs, new MediatorGenerator());
    }

}
