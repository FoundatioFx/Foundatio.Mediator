using Foundatio.Xunit;

namespace Foundatio.Mediator.Tests;

public class MiddlewareGenerationTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void GeneratesBeforeAfterFinally_WithTryFinally()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			public static class MW1Middleware {
				public static void Before(Msg m) { }
				public static void After(Msg m) { }
				public static void Finally(Msg m) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("MW1Middleware.Before", wrapper.Source);
        Assert.Contains("MW1Middleware.After", wrapper.Source);
        Assert.Contains("MW1Middleware.Finally", wrapper.Source);
        Assert.Contains("try", wrapper.Source);
        Assert.Contains("finally", wrapper.Source);
    }

    [Fact]
    public void PassesHandlerResultToAfterAndFinally()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public record Response;

			public class MsgHandler { public Result<Response> Handle(Msg m, CancellationToken ct) => Result.Ok(new Response()); }

			public static class MW1Middleware {
				public static void After(Msg m, Result<Response> result) { }
				public static void Finally(Msg m, Result<Response> result) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("MW1Middleware.After(message, result)", wrapper.Source);
        Assert.Contains("MW1Middleware.Finally(message, result)", wrapper.Source);
    }

    [Fact]
    public void PassesHandlerTupleItemResultToAfterAndFinally()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public record Response;
			public record Event;

			public class MsgHandler { public (Result<Response> Response, Event? Event) Handle(Msg m, CancellationToken ct) => (Result.Ok(new Response()), null); }

			public static class MW1Middleware {
				public static void After(Msg m, Result<Response> result) { }
				public static void Finally(Msg m, Result<Response> result) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("MW1Middleware.After(message, result.Response)", wrapper.Source);
        Assert.Contains("MW1Middleware.Finally(message, result.Response)", wrapper.Source);
    }

    [Fact]
    public void PassesResultOfTToBaseResultParameter()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public record Response;

			public class MsgHandler { public Result<Response> Handle(Msg m, CancellationToken ct) => Result.Ok(new Response()); }

			public static class MW1Middleware {
				public static void After(Msg m, Result result) { }
				public static void Finally(Msg m, Result result) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("MW1Middleware.After(message, result!)", wrapper.Source);
        Assert.Contains("MW1Middleware.Finally(message, result!)", wrapper.Source);
    }

    [Fact]
    public void PassesResultOfTFromTupleToBaseResultParameter()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;
			public record Response;
			public record Event;

			public class MsgHandler { public (Result<Response> Response, Event? Event) Handle(Msg m, CancellationToken ct) => (Result.Ok(new Response()), null); }

			public static class MW1Middleware {
				public static void After(Msg m, Result result) { }
				public static void Finally(Msg m, Result result) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("MW1Middleware.After(message, result.Response!)", wrapper.Source);
        Assert.Contains("MW1Middleware.Finally(message, result.Response!)", wrapper.Source);
    }

    [Fact]
    public void GeneratesAssemblyAttributeForMiddlewareOnly()
    {
        var src = """
			using Foundatio.Mediator;

			public static class LoggingMiddleware
			{
				public static void Before(object message) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        // Should generate assembly attribute even when there are no handlers
        var assemblyAttr = trees.Where(t => t.HintName == "_FoundatioModule.cs").ToList();
        Assert.Single(assemblyAttr);
        Assert.Contains("[assembly: Foundatio.Mediator.FoundatioModule]", assemblyAttr[0].Source);
    }

    [Fact]
    public void GeneratesAssemblyAttributeForHandlers()
    {
        var src = """
			using System.Threading;
			using Foundatio.Mediator;

			public record Msg;
			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        // Should generate assembly attribute when there are handlers
        var assemblyAttr = trees.Where(t => t.HintName == "_FoundatioModule.cs").ToList();
        Assert.Single(assemblyAttr);
        Assert.Contains("[assembly: Foundatio.Mediator.FoundatioModule]", assemblyAttr[0].Source);
    }

    [Fact]
    public void DoesNotGenerateAssemblyAttributeWhenNoHandlersOrMiddleware()
    {
        var src = """
			using Foundatio.Mediator;

			public record Msg;
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        // Should NOT generate assembly attribute when there are no handlers or middleware
        var assemblyAttr = trees.Where(t => t.HintName.Contains("_FoundatioModuleAttribute.g.cs")).ToList();
        Assert.Empty(assemblyAttr);
    }

    #region UseMiddleware Attribute Tests

    [Fact]
    public void UseMiddlewareAttribute_AppliesMiddlewareToHandler()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			[UseMiddleware(typeof(TestMiddleware))]
			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			[Middleware(ExplicitOnly = true)]
			public static class TestMiddleware {
				public static void Before(object m) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("TestMiddleware.Before", wrapper.Source);
    }

    [Fact]
    public void UseMiddlewareAttribute_AppliesMiddlewareToSpecificMethod()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg1;
			public record Msg2;

			public class MsgHandler {
				[UseMiddleware(typeof(TestMiddleware))]
				public void Handle(Msg1 m, CancellationToken ct) { }

				public void Handle(Msg2 m, CancellationToken ct) { }
			}

			[Middleware(ExplicitOnly = true)]
			public static class TestMiddleware {
				public static void Before(object m) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper1 = trees.First(t => t.HintName.Contains("Msg1") && t.HintName.EndsWith("_Handler.g.cs"));
        var wrapper2 = trees.First(t => t.HintName.Contains("Msg2") && t.HintName.EndsWith("_Handler.g.cs"));

        // Msg1 handler should have middleware
        Assert.Contains("TestMiddleware.Before", wrapper1.Source);

        // Msg2 handler should NOT have middleware
        Assert.DoesNotContain("TestMiddleware.Before", wrapper2.Source);
    }

    [Fact]
    public void UseMiddlewareAttribute_MultipleMiddleware_AllApplied()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			[UseMiddleware(typeof(FirstMiddleware))]
			[UseMiddleware(typeof(SecondMiddleware))]
			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			[Middleware(ExplicitOnly = true)]
			public static class FirstMiddleware {
				public static void Before(object m) { }
			}

			[Middleware(ExplicitOnly = true)]
			public static class SecondMiddleware {
				public static void Before(object m) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("FirstMiddleware.Before", wrapper.Source);
        Assert.Contains("SecondMiddleware.Before", wrapper.Source);
    }

    [Fact]
    public void UseMiddlewareAttribute_WithOrder_RespectsOrder()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			[UseMiddleware(typeof(SecondMiddleware), Order = 20)]
			[UseMiddleware(typeof(FirstMiddleware), Order = 10)]
			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			[Middleware(ExplicitOnly = true)]
			public static class FirstMiddleware {
				public static void Before(object m) { }
			}

			[Middleware(ExplicitOnly = true)]
			public static class SecondMiddleware {
				public static void Before(object m) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        // FirstMiddleware should come before SecondMiddleware due to Order
        int firstIndex = wrapper.Source.IndexOf("FirstMiddleware.Before");
        int secondIndex = wrapper.Source.IndexOf("SecondMiddleware.Before");
        Assert.True(firstIndex < secondIndex, "FirstMiddleware should execute before SecondMiddleware based on Order");
    }

    [Fact]
    public void CustomMiddlewareAttribute_WithUseMiddlewareOnClass_AppliesMiddleware()
    {
        // This test verifies the cross-assembly pattern where custom attributes
        // have [UseMiddleware(typeof(X))] applied to them
        var src = """
			using System;
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			[Retry]
			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			// Cross-assembly pattern: [UseMiddleware] attribute on the custom attribute class
			// This allows the source generator to find the middleware type even when
			// the attribute is compiled in a different assembly
			[UseMiddleware(typeof(TestMiddleware))]
			[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
			public class RetryAttribute : Attribute
			{
				public int MaxAttempts { get; set; } = 3;
			}

			[Middleware(ExplicitOnly = true)]
			public static class TestMiddleware {
				public static void Before(object m) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("TestMiddleware.Before", wrapper.Source);
    }

    #endregion

    #region ExplicitOnly Middleware Tests

    [Fact]
    public void ExplicitOnlyMiddleware_NotAppliedGlobally()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			// Middleware with ExplicitOnly = true takes object message type
			// but should NOT be applied to handlers unless explicitly specified
			[Middleware(Order = 50, ExplicitOnly = true)]
			public static class ExplicitOnlyMiddleware {
				public static void Before(object m) { }
			}
			""";

        var (compilation, diagnostics, trees) = RunGenerator(src, [new MediatorGenerator()]);

        // Debug: Check the assembly reference path
        var mediatorAssemblyPath = typeof(IMediator).Assembly.Location;
        var middlewareAttrType = typeof(MiddlewareAttribute);
        var props = middlewareAttrType.GetProperties().Select(p => p.Name).ToList();
        var explicitOnlyProp = middlewareAttrType.GetProperty("ExplicitOnly");

        // Output debug diagnostics
        var debugDiags = diagnostics.Where(d => d.Id.Contains("DEBUG")).ToList();
        if (debugDiags.Any())
        {
            throw new Exception($"Assembly: {mediatorAssemblyPath}\nProps: [{string.Join(", ", props)}]\nExplicitOnly: {explicitOnlyProp?.Name ?? "null"}\n\nDEBUG DIAGNOSTICS:\n{string.Join("\n", debugDiags.Select(d => d.GetMessage()))}");
        }

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));

        // ExplicitOnly middleware should NOT be applied
        Assert.DoesNotContain("ExplicitOnlyMiddleware.Before", wrapper.Source);
    }

    [Fact]
    public void ExplicitOnlyMiddleware_AppliedWhenExplicitlyReferenced()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			[UseMiddleware(typeof(ExplicitOnlyMiddleware))]
			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			[Middleware(ExplicitOnly = true)]
			public static class ExplicitOnlyMiddleware {
				public static void Before(object m) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        // ExplicitOnly middleware SHOULD be applied when explicitly referenced
        Assert.Contains("ExplicitOnlyMiddleware.Before", wrapper.Source);
    }

    [Fact]
    public void GlobalMiddleware_StillAppliedNormally()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			// Global middleware (no ExplicitOnly) takes object message type
			// and SHOULD be applied to all handlers
			public static class GlobalMiddleware {
				public static void Before(object m) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        // Global middleware should be applied
        Assert.Contains("GlobalMiddleware.Before", wrapper.Source);
    }

    [Fact]
    public void MixedGlobalAndExplicitOnly_CorrectlyFiltered()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			// Global middleware - should be applied
			public static class GlobalMiddleware {
				public static void Before(object m) { }
			}

			// ExplicitOnly middleware - should NOT be applied
			[Middleware(ExplicitOnly = true)]
			public static class ExplicitOnlyMiddleware {
				public static void Before(object m) { }
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("GlobalMiddleware.Before", wrapper.Source);
        Assert.DoesNotContain("ExplicitOnlyMiddleware.Before", wrapper.Source);
    }

    #endregion

    #region ExecuteAsync Middleware Tests

    [Fact]
    public void ExecuteAsyncMiddleware_GeneratesCorrectCode()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			public static class ExecuteMiddleware {
				public static async ValueTask<object?> ExecuteAsync(Msg message, HandlerExecutionDelegate next) {
					return await next();
				}
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("ExecuteMiddleware.ExecuteAsync", wrapper.Source);
    }

    [Fact]
    public void ExecuteAsyncMiddleware_WithHandlerExecutionInfo_GeneratesCorrectCode()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			public static class ExecuteWithInfoMiddleware {
				public static async ValueTask<object?> ExecuteAsync(
					Msg message,
					HandlerExecutionDelegate next,
					HandlerExecutionInfo handlerInfo) {
					// Can access handler type and method info
					return await next();
				}
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("ExecuteWithInfoMiddleware.ExecuteAsync", wrapper.Source);
    }

    [Fact]
    public void ExecuteAsyncMiddleware_TakesObjectType_AppliesGlobally()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg;

			public class MsgHandler { public void Handle(Msg m, CancellationToken ct) { } }

			public static class GlobalExecuteMiddleware {
				public static async ValueTask<object?> ExecuteAsync(object message, HandlerExecutionDelegate next) {
					return await next();
				}
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper = trees.First(t => t.HintName.EndsWith("_Handler.g.cs"));
        Assert.Contains("GlobalExecuteMiddleware.ExecuteAsync", wrapper.Source);
    }

    [Fact]
    public void ExecuteAsyncMiddleware_WithExplicitOnly_OnlyAppliedWhenReferenced()
    {
        var src = """
			using System.Threading;
			using System.Threading.Tasks;
			using Foundatio.Mediator;

			public record Msg1;
			public record Msg2;

			[UseMiddleware(typeof(ExplicitExecuteMiddleware))]
			public class Msg1Handler { public void Handle(Msg1 m, CancellationToken ct) { } }

			public class Msg2Handler { public void Handle(Msg2 m, CancellationToken ct) { } }

			[Middleware(ExplicitOnly = true)]
			public static class ExplicitExecuteMiddleware {
				public static async ValueTask<object?> ExecuteAsync(object message, HandlerExecutionDelegate next) {
					return await next();
				}
			}
			""";

        var (_, _, trees) = RunGenerator(src, [new MediatorGenerator()]);

        var wrapper1 = trees.First(t => t.HintName.Contains("Msg1") && t.HintName.EndsWith("_Handler.g.cs"));
        var wrapper2 = trees.First(t => t.HintName.Contains("Msg2") && t.HintName.EndsWith("_Handler.g.cs"));

        Assert.Contains("ExplicitExecuteMiddleware.ExecuteAsync", wrapper1.Source);
        Assert.DoesNotContain("ExplicitExecuteMiddleware.ExecuteAsync", wrapper2.Source);
    }

    #endregion
}


