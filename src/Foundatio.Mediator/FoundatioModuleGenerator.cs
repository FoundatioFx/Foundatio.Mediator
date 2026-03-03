using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class FoundatioModuleGenerator
{
    /// <summary>
    /// Generates the [assembly: FoundatioModule] attribute to mark assemblies that contain handlers or middleware.
    /// This attribute is used by MetadataMiddlewareScanner to discover middleware in referenced assemblies.
    /// Also generates the AddHandlers extension method for DI registration.
    /// </summary>
    public static void Execute(SourceProductionContext context, CompilationInfo compilationInfo, List<HandlerInfo> handlers, ImmutableArray<MiddlewareInfo> middleware, GeneratorConfiguration configuration)
    {
        var assemblyName = compilationInfo.AssemblyName;
        var safeAssemblyName = assemblyName.ToIdentifier();
        if (string.IsNullOrEmpty(safeAssemblyName))
            safeAssemblyName = Guid.NewGuid().ToString("N").Substring(0, 10);
        var className = $"{safeAssemblyName}_MediatorHandlers";
        const string hintName = "_FoundatioModule.cs";

        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, hintName);

        // Check if we need DI usings (for handlers or non-static middleware with lifetime)
        bool hasHandlers = handlers.Count > 0;
        bool hasMiddlewareToRegister = middleware.Any(m => !m.IsStatic && ShouldRegisterMiddleware(m, configuration));

        if (hasHandlers || hasMiddlewareToRegister)
        {
            source.AppendLine();
            source.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            source.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
            source.AppendLine("using System;");
            source.AppendLine("using System.Diagnostics;");
            source.AppendLine("using System.Diagnostics.CodeAnalysis;");
            source.AppendLine("using System.Threading;");
            source.AppendLine("using System.Threading.Tasks;");
            source.AppendLine("using Foundatio.Mediator.Generated;");
        }

        source.AppendLine();
        source.AppendLine("[assembly: Foundatio.Mediator.FoundatioModule]");
        source.AppendLine();

        if (hasHandlers || hasMiddlewareToRegister)
        {
            source.AppendLine("namespace Foundatio.Mediator;");
            source.AppendLine();
            source.AddGeneratedCodeAttribute();
            source.AppendLine("[ExcludeFromCodeCoverage]");
            source.AppendLine($"public static class {className}");
            source.AppendLine("{");
            source.AppendLine($"    public static NotificationPublishStrategy PublishStrategy {{ get; }} = NotificationPublishStrategy.{configuration.NotificationPublishStrategy};");
            source.AppendLine();
            source.AppendLine("    public static void AddHandlers(IServiceCollection services, HandlerRegistry registry)");
            source.AppendLine("    {");
            source.IncrementIndent().IncrementIndent();

            // Register middleware first (they may be used by multiple handlers)
            if (hasMiddlewareToRegister)
            {
                source.AppendLine("// Register middleware instances");
                foreach (var m in middleware.Where(m => !m.IsStatic && ShouldRegisterMiddleware(m, configuration)))
                {
                    string effectiveLifetime = m.Lifetime ?? configuration.DefaultMiddlewareLifetime;
                    string lifetimeMethod = GetLifetimeMethod(effectiveLifetime);
                    source.AppendLine($"services.{lifetimeMethod}<{m.FullName}>();");
                }
                source.AppendLine();
            }

            if (hasHandlers)
            {
                source.AppendLine("// Register HandlerRegistration instances keyed by message type name");
                source.AppendLine();

                foreach (var handler in handlers)
                {
                    string handlerClassName = HandlerGenerator.GetHandlerClassName(handler);

                    // Determine lifetime: use handler-specific lifetime if set, otherwise fall back to default
                    // Handler.Lifetime is null when not specified via attribute (use project default)
                    // Handler.Lifetime is set to "Transient"/"Scoped"/"Singleton" when explicitly specified
                    string effectiveLifetime = handler.Lifetime ?? configuration.DefaultHandlerLifetime;
                    bool shouldRegisterHandler = !String.Equals(effectiveLifetime, "None", StringComparison.OrdinalIgnoreCase);
                    string? lifetimeMethod = shouldRegisterHandler ? GetLifetimeMethod(effectiveLifetime) : null;

                    if (handler is { IsStatic: false, IsGenericHandlerClass: false } && lifetimeMethod != null)
                    {
                        source.AppendLine($"services.{lifetimeMethod}<{handler.FullName}>();");
                    }

                    if (handler.IsGenericHandlerClass)
                    {
                        if (handler is not { MessageGenericTypeDefinitionFullName: not null, GenericArity: > 0 })
                            continue;

                        string genericArity = handler.GenericArity switch
                        {
                            1 => "<>",
                            2 => "<,>",
                            3 => "<,,>",
                            4 => "<,,,>",
                            5 => "<,,,,>",
                            6 => "<,,,,,>",
                            7 => "<,,,,,,>",
                            8 => "<,,,,,,,>",
                            9 => "<,,,,,,,,>",
                            10 => "<,,,,,,,,,>",
                            _ => $"<{new string(',', handler.GenericArity - 1)}>" // fallback
                        };

                        string wrapperTypeOf = $"typeof({handlerClassName}{genericArity})";
                        string msgTypeOf = $"typeof({handler.MessageGenericTypeDefinitionFullName})";
                        if (!handler.IsStatic && lifetimeMethod != null)
                        {
                            string handlerFullName = handler.FullName;
                            int index = handlerFullName.IndexOf('<');
                            if (index > 0)
                                handlerFullName = handlerFullName.Substring(0, index);
                            source.AppendLine($"services.{lifetimeMethod}(typeof({handlerFullName}{genericArity}));");

                        }

                        source.AppendLine($"registry.AddOpenGenericHandler(new OpenGenericHandlerDescriptor({msgTypeOf}, {wrapperTypeOf}, {handler.IsAsync.ToString().ToLower()}));");
                    }
                    else
                    {
                        source.AppendLine($"registry.AddHandler(new HandlerRegistration(");
                        source.AppendLine($"        MessageTypeKey.Get(typeof({handler.MessageType.FullName})),");
                        source.AppendLine($"        \"{handlerClassName}\",");

                        if (handler.IsAsync)
                        {
                            source.AppendLine($"        {handlerClassName}.UntypedHandleAsync,");
                            source.AppendLine($"        null,");
                        }
                        else
                        {
                            source.AppendLine($"        (mediator, message, cancellationToken, responseType) => new ValueTask<object?>({handlerClassName}.UntypedHandle(mediator, message, cancellationToken, responseType)),");
                            source.AppendLine($"        {handlerClassName}.UntypedHandle,");
                        }

                        source.AppendLine($"        {handler.IsAsync.ToString().ToLower()},");

                        // Emit order and ordering constraints
                        var hasOrderConstraints = handler.OrderBefore.Any() || handler.OrderAfter.Any();
                        if (hasOrderConstraints)
                        {
                            source.AppendLine($"        {handler.Order},");
                            source.AppendLine($"        orderBefore: {FormatStringArray(handler.OrderBefore)},");
                            source.AppendLine($"        orderAfter: {FormatStringArray(handler.OrderAfter)}));");
                        }
                        else
                        {
                            source.AppendLine($"        {handler.Order}));");
                        }
                        source.AppendLine();
                    }
                }
            }

            // Register HttpContext-based authorization context provider when ASP.NET Core is available
            // and at least one handler actually requires authorization
            bool anyHandlerRequiresAuth = handlers.Any(h => h.RequiresAuthorization);
            if (configuration.AuthorizationEnabled && compilationInfo.IsAspNetCore && anyHandlerRequiresAuth)
            {
                source.AppendLine("// Ensure IHttpContextAccessor is available for the authorization context provider");
                source.AppendLine("services.AddHttpContextAccessor();");
                source.AppendLine("// Register HttpContext-based authorization context provider for ASP.NET Core");
                source.AppendLine("services.TryAddSingleton<Foundatio.Mediator.IAuthorizationContextProvider, HttpContextAuthorizationContextProvider>();");
            }

            source.DecrementIndent();
            source.AppendLine("}");

            source.DecrementIndent();
            source.AppendLine("}");

            // Emit inline HttpContextAuthorizationContextProvider when ASP.NET Core is available
            if (configuration.AuthorizationEnabled && compilationInfo.IsAspNetCore && anyHandlerRequiresAuth)
            {
                source.AppendLine();
                source.AddGeneratedCodeAttribute();
                source.AppendLine("[ExcludeFromCodeCoverage]");
                source.AppendLines("""
                    internal sealed class HttpContextAuthorizationContextProvider : Foundatio.Mediator.IAuthorizationContextProvider
                    {
                        private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;

                        public HttpContextAuthorizationContextProvider(Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
                        {
                            _httpContextAccessor = httpContextAccessor ?? throw new System.ArgumentNullException(nameof(httpContextAccessor));
                        }

                        public System.Security.Claims.ClaimsPrincipal? GetCurrentPrincipal()
                        {
                            return _httpContextAccessor.HttpContext?.User;
                        }
                    }
                    """);
            }
        }

        context.AddSource(hintName, source.ToString());
    }

    private static bool ShouldRegisterMiddleware(MiddlewareInfo middleware, GeneratorConfiguration configuration)
    {
        string effectiveLifetime = middleware.Lifetime ?? configuration.DefaultMiddlewareLifetime;
        return !String.Equals(effectiveLifetime, "None", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLifetimeMethod(string lifetime)
    {
        if (String.Equals(lifetime, "Transient", StringComparison.OrdinalIgnoreCase))
            return "TryAddTransient";
        if (String.Equals(lifetime, "Scoped", StringComparison.OrdinalIgnoreCase))
            return "TryAddScoped";
        if (String.Equals(lifetime, "Singleton", StringComparison.OrdinalIgnoreCase))
            return "TryAddSingleton";
        // None or unknown - default to Singleton for performance
        return "TryAddSingleton";
    }

    private static string FormatStringArray(IEnumerable<string> items)
    {
        var values = items.ToList();
        if (values.Count == 0)
            return "null";

        var escaped = string.Join(", ", values.Select(v => $"\"{v}\""));
        return $"new[] {{ {escaped} }}";
    }
}
