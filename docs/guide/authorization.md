# Authorization

Foundatio.Mediator provides built-in, unified authorization that works for **both** HTTP endpoints and direct `mediator.InvokeAsync()` calls. Authorization requirements are baked into the handler's `HandlerExecutionInfo` at compile time, ensuring zero-reflection enforcement at runtime.

::: tip Events Skip Authorization
Authorization only runs on the **invoke** path (`InvokeAsync` / `Invoke`). Handlers triggered via `PublishAsync` or [cascading tuple returns](/guide/cascading-messages) always skip auth checks because events represent something that has already happened — blocking an event handler would leave the system in an inconsistent state. If the event handler itself calls `mediator.InvokeAsync(...)` internally, that nested invoke **will** enforce authorization as normal.
:::

## Quick Start

Add `[HandlerAuthorize]` to any handler that requires authentication:

```csharp
using Foundatio.Mediator;

[HandlerAuthorize]
public class SecureHandler
{
    public Task<Result<Secret>> HandleAsync(GetSecret query, ISecretStore store, CancellationToken ct)
    {
        return store.GetAsync(query.Id, ct);
    }
}
```

That's it. The source generator emits an authorization check before the handler runs. If the caller isn't authenticated, Result-returning handlers receive `Result.Unauthorized()` and non-Result handlers throw `UnauthorizedAccessException`.

## How It Works

1. **Compile time** — The source generator reads `[HandlerAuthorize]` and `[HandlerAllowAnonymous]` attributes and assembly-level `AuthorizationRequired`/`AuthorizationPolicies`/`AuthorizationRoles` properties, then bakes the requirements into the generated handler wrapper as an `AuthorizationRequirements` instance on `HandlerExecutionInfo`.

2. **Runtime (invoke path only)** — Before calling the handler method via `InvokeAsync`/`Invoke`, the generated code:
   - Resolves `IAuthorizationContextProvider` to get the current `ClaimsPrincipal`
   - Resolves `IHandlerAuthorizationService` to perform the check
   - Calls `AuthorizeAsync(principal, requirements, cancellationToken)`
   - Short-circuits with the appropriate unauthorized/forbidden result if the check fails

3. **Zero overhead when not used** — If a handler has no authorization requirements, no authorization code is generated at all.

## Attributes

### `[HandlerAuthorize]`

Apply to a handler **class** or **method** to require authorization:

```csharp
// Class-level: all methods in this handler require auth
[HandlerAuthorize]
public class AdminHandler
{
    public Task<Result> HandleAsync(DeleteUser command) { ... }
    public Task<Result<User>> HandleAsync(GetUser query) { ... }
}

// Method-level: only this specific handler requires auth
public class MixedHandler
{
    [HandlerAuthorize]
    public Task<Result> HandleAsync(SensitiveCommand command) { ... }

    public Task<Result<PublicData>> HandleAsync(PublicQuery query) { ... }
}
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Roles` | `string[]?` | Array of required roles (any-of semantics) |
| `Policies` | `string[]?` | Array of authorization policy names to evaluate |

```csharp
[HandlerAuthorize(Roles = ["Admin", "Manager"])]
public class AdminHandler { ... }

[HandlerAuthorize(Policies = ["CanEditProducts", "IsVerified"])]
public class ProductHandler { ... }

[HandlerAuthorize(Roles = ["Admin"], Policies = ["MfaRequired"])]
public class HighSecurityHandler { ... }
```

### `[HandlerAllowAnonymous]`

Apply to a handler class or method to bypass authorization, even when global `AuthorizationRequired = true` is set:

```csharp
[HandlerAllowAnonymous]
public class PublicHandler
{
    public Task<Result<Status>> HandleAsync(HealthCheck query) => ...;
}
```

ASP.NET Core's `[AllowAnonymous]` attribute is also recognized and has the same effect.

## Global Configuration

Set `AuthorizationRequired = true` on the assembly attribute to require auth for all handlers by default:

```csharp
[assembly: MediatorConfiguration(
    AuthorizationRequired = true,
    AuthorizationPolicies = ["DefaultPolicy"],
    AuthorizationRoles = "User"
)]
```

Then use `[HandlerAllowAnonymous]` to opt out specific handlers:

```csharp
[HandlerAllowAnonymous]
public class HealthHandler
{
    public string Handle(HealthCheck query) => "OK";
}
```

### Precedence

Authorization requirements are resolved in this order (most specific wins):

1. **Method-level** `[HandlerAuthorize]` or `[HandlerAllowAnonymous]`
2. **Class-level** `[HandlerAuthorize]` or `[HandlerAllowAnonymous]`
3. **Assembly-level** `AuthorizationRequired` / `AuthorizationPolicies` / `AuthorizationRoles`

If a handler has an explicit `[HandlerAuthorize]` or `[HandlerAllowAnonymous]`, the assembly-level defaults are not merged in.

## Authorization Result Handling

The behavior when authorization fails depends on the handler's return type:

| Return Type | On Unauthorized | On Forbidden |
|-------------|-----------------|--------------|
| `Result`, `Result<T>` | `Result.Unauthorized("Authentication required.")` | `Result.Forbidden("Access denied.")` |
| Other types | `throw UnauthorizedAccessException` | `throw UnauthorizedAccessException` |

The distinction between **Unauthorized** (not authenticated) and **Forbidden** (authenticated but lacking permissions) is made by the authorization service based on whether a principal is present.

## Extensibility

### IAuthorizationContextProvider

Provides the `ClaimsPrincipal` for the current execution context:

```csharp
public interface IAuthorizationContextProvider
{
    ClaimsPrincipal? GetCurrentPrincipal();
}
```

**Auto-registration:** In ASP.NET Core apps (where `IHttpContextAccessor` is available), a provider that reads from `HttpContext.User` is automatically registered. For non-web scenarios, implement and register your own:

```csharp
public class WorkerAuthProvider : IAuthorizationContextProvider
{
    public ClaimsPrincipal? GetCurrentPrincipal()
    {
        // Return a service identity or read from ambient context
        return new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "worker-service") },
            "ServiceAuth"));
    }
}

// Register in DI
services.AddSingleton<IAuthorizationContextProvider, WorkerAuthProvider>();
```

### IHandlerAuthorizationService

Performs the actual authorization check:

```csharp
public interface IHandlerAuthorizationService
{
    ValueTask<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal? principal,
        AuthorizationRequirements? requirements,
        CancellationToken cancellationToken = default);
}
```

The default implementation checks:
- Whether the principal is authenticated (identity is not null and `IsAuthenticated` is true)
- Whether the principal has the required roles (via `IsInRole`)
- Whether the principal has claims matching the required policies

Replace it to integrate with your own authorization system:

```csharp
public class CustomAuthService : IHandlerAuthorizationService
{
    private readonly IAuthorizationService _aspNetAuth;

    public CustomAuthService(IAuthorizationService aspNetAuth)
    {
        _aspNetAuth = aspNetAuth;
    }

    public async ValueTask<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal? principal,
        AuthorizationRequirements? requirements,
        CancellationToken cancellationToken = default)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return AuthorizationResult.Unauthorized();

        if (requirements?.Policies != null)
        {
            foreach (var policy in requirements.Policies)
            {
                var result = await _aspNetAuth.AuthorizeAsync(principal, policy);
                if (!result.Succeeded)
                    return AuthorizationResult.Forbidden($"Policy '{policy}' not satisfied.");
            }
        }

        return AuthorizationResult.Success();
    }
}

services.AddSingleton<IHandlerAuthorizationService, CustomAuthService>();
```

## Events and Publish

Authorization is **not enforced** when a handler is triggered through the publish (event) path. This includes:

- Direct calls to `mediator.PublishAsync(message)`
- Cascading messages from [tuple returns](/guide/cascading-messages) (e.g., `(Result<Order>, OrderCreatedEvent)`)
- Distributed notifications arriving from other services

Events represent facts — something that has already happened. Blocking an event handler with an authorization failure would leave the system in an inconsistent state (the action succeeded but side effects didn't run). Authorization should be enforced at the point where the action is **requested** (the `InvokeAsync` call), not when downstream handlers react to it.

If an event handler needs to perform a privileged operation internally, it can call `mediator.InvokeAsync(...)` — that nested invoke will enforce authorization normally.

## Middleware vs Built-in Authorization

You can still use middleware for authorization if you prefer:

```csharp
[Middleware(Order = 0)]
public class AuthorizationMiddleware
{
    public HandlerResult Before(object message, HandlerExecutionInfo info)
    {
        if (!IsAuthorized(message, info))
            return HandlerResult.ShortCircuit(Result.Unauthorized());

        return HandlerResult.Continue();
    }
}
```

**When to use built-in authorization:** For standard role/policy-based checks that follow a consistent pattern across handlers.

**When to use middleware:** For complex, cross-cutting authorization logic that needs access to the full pipeline context, or when you need to authorize based on the message content itself.
