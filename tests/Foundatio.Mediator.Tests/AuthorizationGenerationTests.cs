namespace Foundatio.Mediator.Tests;

/// <summary>
/// Verifies the source generator emits correct authorization check code
/// for handlers with [HandlerAuthorize], [HandlerAllowAnonymous], roles, and policies.
/// Covers both Result&lt;T&gt; returning handlers (which return Result.Forbidden/Unauthorized)
/// and non-Result handlers (which throw UnauthorizedAccessException).
/// </summary>
public class AuthorizationGenerationTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    private static readonly MediatorGenerator Gen = new();

    // ── Result<T> handler with [HandlerAuthorize] ──────────────────────────

    [Fact]
    public void HandlerAuthorize_ResultHandler_EmitsAuthCheckWithResultReturn()
    {
        var source = """
            using Foundatio.Mediator;

            public record GetOrder(string Id);
            public record OrderView(string Id);

            [HandlerAuthorize]
            public class OrderHandler
            {
                public Result<OrderView> Handle(GetOrder query) => new OrderView(query.Id);
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("OrderHandler_GetOrder")).Source;

        Assert.NotNull(handlerSource);

        // Auth services resolved from DI
        Assert.Contains("GetRequiredService<Foundatio.Mediator.IAuthorizationContextProvider>()", handlerSource);
        Assert.Contains("GetRequiredService<Foundatio.Mediator.IHandlerAuthorizationService>()", handlerSource);
        Assert.Contains("authContextProvider.GetCurrentPrincipal()", handlerSource);
        Assert.Contains("authService.AuthorizeAsync(principal, handlerExecutionInfo.Authorization, cancellationToken)", handlerSource);

        // Result<T> handler returns Result.Forbidden / Result.Unauthorized instead of throwing
        Assert.Contains("Result.Forbidden(authResult.FailureReason", handlerSource);
        Assert.Contains("Result.Unauthorized(authResult.FailureReason", handlerSource);

        // Should NOT throw UnauthorizedAccessException for Result handlers
        Assert.DoesNotContain("UnauthorizedAccessException", handlerSource);
    }

    // ── Non-Result handler with [HandlerAuthorize] ─────────────────────────

    [Fact]
    public void HandlerAuthorize_NonResultHandler_EmitsAuthCheckWithThrow()
    {
        var source = """
            using Foundatio.Mediator;

            public record DeleteOrder(string Id);

            [HandlerAuthorize]
            public class OrderHandler
            {
                public void Handle(DeleteOrder command) { }
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("OrderHandler_DeleteOrder")).Source;

        Assert.NotNull(handlerSource);

        // Auth services resolved from DI
        Assert.Contains("GetRequiredService<Foundatio.Mediator.IAuthorizationContextProvider>()", handlerSource);
        Assert.Contains("GetRequiredService<Foundatio.Mediator.IHandlerAuthorizationService>()", handlerSource);

        // Non-Result handler throws UnauthorizedAccessException
        Assert.Contains("UnauthorizedAccessException", handlerSource);

        // Should NOT return Result.Forbidden/Unauthorized for non-Result handlers
        Assert.DoesNotContain("Result.Forbidden", handlerSource);
        Assert.DoesNotContain("Result.Unauthorized", handlerSource);
    }

    // ── Roles baked into HandlerExecutionInfo ──────────────────────────────

    [Fact]
    public void HandlerAuthorize_WithRoles_BakesRolesIntoExecutionInfo()
    {
        var source = """
            using Foundatio.Mediator;

            public record CreateOrder(string Name);
            public record OrderView(string Id, string Name);

            public class OrderHandler
            {
                [HandlerAuthorize(Roles = ["Admin", "Manager"])]
                public Result<OrderView> Handle(CreateOrder cmd) => new OrderView("1", cmd.Name);
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("OrderHandler_CreateOrder")).Source;

        Assert.NotNull(handlerSource);

        // AuthorizationRequirements constructor with roles
        Assert.Contains("new Foundatio.Mediator.AuthorizationRequirements(true", handlerSource);
        Assert.Contains("\"Admin\"", handlerSource);
        Assert.Contains("\"Manager\"", handlerSource);
    }

    // ── Policies baked into HandlerExecutionInfo ───────────────────────────

    [Fact]
    public void HandlerAuthorize_WithPolicy_BakesPolicyIntoExecutionInfo()
    {
        var source = """
            using Foundatio.Mediator;

            public record UpdateOrder(string Id);

            public class OrderHandler
            {
                [HandlerAuthorize(Policies = ["CanEditOrders"])]
                public void Handle(UpdateOrder cmd) { }
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("OrderHandler_UpdateOrder")).Source;

        Assert.NotNull(handlerSource);

        Assert.Contains("new Foundatio.Mediator.AuthorizationRequirements(true", handlerSource);
        Assert.Contains("\"CanEditOrders\"", handlerSource);
    }

    // ── [HandlerAllowAnonymous] skips auth code ───────────────────────────

    [Fact]
    public void HandlerAllowAnonymous_OnMethod_SkipsAuthCode()
    {
        var source = """
            using Foundatio.Mediator;

            public record GetPublicInfo();

            [HandlerAuthorize]
            public class InfoHandler
            {
                [HandlerAllowAnonymous]
                public string Handle(GetPublicInfo query) => "public";
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("InfoHandler_GetPublicInfo")).Source;

        Assert.NotNull(handlerSource);

        // [HandlerAllowAnonymous] on method should skip auth code entirely
        Assert.DoesNotContain("IAuthorizationContextProvider", handlerSource);
        Assert.DoesNotContain("IHandlerAuthorizationService", handlerSource);
        Assert.DoesNotContain("AuthorizeAsync", handlerSource);
    }

    // ── No auth attributes = no auth code ─────────────────────────────────

    [Fact]
    public void NoAuthAttributes_NoAuthCodeGenerated()
    {
        var source = """
            using Foundatio.Mediator;

            public record GetItem(string Id);

            public class ItemHandler
            {
                public string Handle(GetItem query) => "item";
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("ItemHandler_GetItem")).Source;

        Assert.NotNull(handlerSource);

        // No auth code should be emitted
        Assert.DoesNotContain("IAuthorizationContextProvider", handlerSource);
        Assert.DoesNotContain("IHandlerAuthorizationService", handlerSource);
        Assert.DoesNotContain("AuthorizeAsync", handlerSource);
        Assert.DoesNotContain("AuthorizationRequirements", handlerSource);
    }

    // ── Assembly-level AuthorizationRequired ───────────────────────────────

    [Fact]
    public void AssemblyAuthorizationRequired_ResultHandler_EmitsAuthCode()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(AuthorizationRequired = true)]

            public record GetWidget(string Id);
            public record WidgetView(string Id);

            public class WidgetHandler
            {
                public Result<WidgetView> Handle(GetWidget query) => new WidgetView(query.Id);
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("WidgetHandler_GetWidget")).Source;

        Assert.NotNull(handlerSource);

        // Assembly-level AuthorizationRequired should cause auth code to be emitted
        Assert.Contains("GetRequiredService<Foundatio.Mediator.IAuthorizationContextProvider>()", handlerSource);
        Assert.Contains("AuthorizeAsync", handlerSource);

        // Result handler should return Result.Forbidden/Unauthorized
        Assert.Contains("Result.Forbidden", handlerSource);
        Assert.Contains("Result.Unauthorized", handlerSource);
    }

    [Fact]
    public void AssemblyAuthorizationRequired_NonResultHandler_EmitsThrow()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(AuthorizationRequired = true)]

            public record ProcessItem(string Id);

            public class ItemHandler
            {
                public void Handle(ProcessItem command) { }
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("ItemHandler_ProcessItem")).Source;

        Assert.NotNull(handlerSource);

        // Non-Result handler with assembly-level auth should throw
        Assert.Contains("UnauthorizedAccessException", handlerSource);
        Assert.DoesNotContain("Result.Forbidden", handlerSource);
    }

    // ── Assembly-level auth with [HandlerAllowAnonymous] override ─────────

    [Fact]
    public void AssemblyAuth_WithHandlerAllowAnonymous_SkipsAuthCode()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(AuthorizationRequired = true)]

            public record GetHealth();

            [HandlerAllowAnonymous]
            public class HealthHandler
            {
                public string Handle(GetHealth query) => "healthy";
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("HealthHandler_GetHealth")).Source;

        Assert.NotNull(handlerSource);

        // [HandlerAllowAnonymous] overrides assembly-level auth
        Assert.DoesNotContain("IAuthorizationContextProvider", handlerSource);
        Assert.DoesNotContain("AuthorizeAsync", handlerSource);
    }

    // ── Class-level auth with method-level roles ──────────────────────────

    [Fact]
    public void ClassAuth_MethodRolesOverride_UsesMethodRoles()
    {
        var source = """
            using Foundatio.Mediator;

            public record DeleteUser(string Id);

            [HandlerAuthorize(Roles = new[] { "User" })]
            public class UserHandler
            {
                [HandlerAuthorize(Roles = ["Admin"])]
                public void Handle(DeleteUser cmd) { }
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("UserHandler_DeleteUser")).Source;

        Assert.NotNull(handlerSource);

        // Method-level roles should take precedence
        Assert.Contains("\"Admin\"", handlerSource);
        // Class-level role should NOT appear because method-level takes precedence
        Assert.DoesNotContain("\"User\"", handlerSource);
    }

    // ── Task<Result<T>> async handler ─────────────────────────────────────

    [Fact]
    public void HandlerAuthorize_AsyncResultHandler_EmitsResultReturn()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record FindOrder(string Id);
            public record OrderView(string Id);

            [HandlerAuthorize(Roles = ["Admin"])]
            public class OrderHandler
            {
                public async Task<Result<OrderView>> HandleAsync(FindOrder query, CancellationToken ct)
                {
                    await Task.CompletedTask;
                    return new OrderView(query.Id);
                }
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("OrderHandler_FindOrder")).Source;

        Assert.NotNull(handlerSource);

        // Async Result handler should also use Result.Forbidden/Unauthorized
        Assert.Contains("Result.Forbidden", handlerSource);
        Assert.Contains("Result.Unauthorized", handlerSource);
        Assert.DoesNotContain("UnauthorizedAccessException", handlerSource);
    }

    // ── Task (non-Result) async handler ───────────────────────────────────

    [Fact]
    public void HandlerAuthorize_AsyncNonResultHandler_EmitsThrow()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record SendEmail(string To, string Body);

            [HandlerAuthorize]
            public class EmailHandler
            {
                public async Task HandleAsync(SendEmail cmd, CancellationToken ct)
                {
                    await Task.CompletedTask;
                }
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("EmailHandler_SendEmail")).Source;

        Assert.NotNull(handlerSource);

        // Async non-Result handler should throw
        Assert.Contains("UnauthorizedAccessException", handlerSource);
        Assert.DoesNotContain("Result.Forbidden", handlerSource);
    }

    // ── Assembly-level roles ──────────────────────────────────────────────

    [Fact]
    public void AssemblyAuthorizationRequired_WithRoles_BakesRolesIntoExecutionInfo()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                AuthorizationRequired = true,
                AuthorizationRoles = ["Admin"]
            )]

            public record GetDashboard();

            public class DashboardHandler
            {
                public string Handle(GetDashboard query) => "data";
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("DashboardHandler_GetDashboard")).Source;

        Assert.NotNull(handlerSource);

        Assert.Contains("new Foundatio.Mediator.AuthorizationRequirements(true", handlerSource);
        Assert.Contains("\"Admin\"", handlerSource);
    }

    // ── DisableAuthorization skips auth checks ─────────────────────────────

    [Fact]
    public void DisableAuthorization_ResultHandler_SkipsAuthCheck()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(DisableAuthorization = true)]

            public record GetOrder(string Id);
            public record OrderView(string Id);

            [HandlerAuthorize(Roles = ["Admin"])]
            public class OrderHandler
            {
                public Result<OrderView> Handle(GetOrder query) => new OrderView(query.Id);
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("OrderHandler_GetOrder")).Source;

        Assert.NotNull(handlerSource);

        // No auth check code should be emitted
        Assert.DoesNotContain("IAuthorizationContextProvider", handlerSource);
        Assert.DoesNotContain("IHandlerAuthorizationService", handlerSource);
        Assert.DoesNotContain("AuthorizeAsync", handlerSource);
        Assert.DoesNotContain("Result.Forbidden", handlerSource);
        Assert.DoesNotContain("Result.Unauthorized", handlerSource);
    }

    [Fact]
    public void DisableAuthorization_NonResultHandler_SkipsAuthCheck()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(DisableAuthorization = true)]

            public record DeleteOrder(string Id);

            [HandlerAuthorize]
            public class OrderHandler
            {
                public void Handle(DeleteOrder command) { }
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("OrderHandler_DeleteOrder")).Source;

        Assert.NotNull(handlerSource);

        // No auth check code should be emitted
        Assert.DoesNotContain("IAuthorizationContextProvider", handlerSource);
        Assert.DoesNotContain("IHandlerAuthorizationService", handlerSource);
        Assert.DoesNotContain("UnauthorizedAccessException", handlerSource);
    }

    [Fact]
    public void DisableAuthorization_SkipsHttpContextAccessorRegistration()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(DisableAuthorization = true, AuthorizationRequired = true)]

            public record GetOrder(string Id);

            public class OrderHandler
            {
                public string Handle(GetOrder query) => "ok";
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var moduleSource = trees.FirstOrDefault(t => t.HintName == "_FoundatioModule.g.cs").Source;

        Assert.NotNull(moduleSource);

        // No HttpContextAccessor or auth provider registration
        Assert.DoesNotContain("AddHttpContextAccessor", moduleSource);
        Assert.DoesNotContain("HttpContextAuthorizationContextProvider", moduleSource);
        Assert.DoesNotContain("IAuthorizationContextProvider", moduleSource);
    }

    [Fact]
    public void DisableAuthorization_AssemblyAuthRequired_StillSkipsAuthChecks()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                DisableAuthorization = true,
                AuthorizationRequired = true,
                AuthorizationRoles = ["Admin"]
            )]

            public record GetDashboard();

            public class DashboardHandler
            {
                public string Handle(GetDashboard query) => "data";
            }
            """;

        var (_, _, trees) = RunGenerator(source, [Gen]);
        var handlerSource = trees.FirstOrDefault(t => t.HintName.Contains("DashboardHandler_GetDashboard")).Source;

        Assert.NotNull(handlerSource);

        // Auth checks should NOT be emitted even with assembly-level auth
        Assert.DoesNotContain("IAuthorizationContextProvider", handlerSource);
        Assert.DoesNotContain("IHandlerAuthorizationService", handlerSource);
        Assert.DoesNotContain("AuthorizeAsync", handlerSource);
    }
}
