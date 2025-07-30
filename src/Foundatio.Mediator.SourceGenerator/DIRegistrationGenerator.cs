using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class DIRegistrationGenerator
{
    public static string GenerateDIRegistration(List<HandlerInfo> handlers, List<MiddlewareInfo> middlewares)
    {
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader();

        source.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        source.AppendLine("using System;");
        source.AppendLine("using System.Diagnostics;");
        source.AppendLine("using System.Diagnostics.CodeAnalysis;");
        source.AppendLine("using System.Threading;");
        source.AppendLine("using System.Threading.Tasks;");
        source.AppendLine();
        source.AppendLine("namespace Foundatio.Mediator;");
        source.AppendLine();
        source.AppendLine("[DebuggerStepThrough]");
        source.AppendLine("[DebuggerNonUserCode]");
        source.AppendLine("[ExcludeFromCodeCoverage]");
        source.AppendLine("public static partial class ServiceCollectionExtensions");
        source.AppendLine("{");
        source.AppendLine("    [DebuggerStepThrough]");
        source.AppendLine("    public static IServiceCollection AddMediator(this IServiceCollection services)");
        source.AppendLine("    {");
        source.AppendLine("        services.AddSingleton<IMediator, Mediator>();");
        source.AppendLine();
        source.AppendLine("        // Register HandlerRegistration instances keyed by message type name");
        source.AppendLine("        // Note: Handlers themselves are NOT auto-registered in DI");
        source.AppendLine("        // Users can register them manually if they want specific lifetimes");

        source.AppendLine();

        foreach (var handler in handlers)
        {
            string wrapperClassName = HandlerWrapperGenerator.GetWrapperClassName(handler);

            // Check if this handler effectively needs to be async due to middleware
            bool isEffectivelyAsync = IsHandlerEffectivelyAsync(handler, middlewares);

            source.AppendLine($"        services.AddKeyedSingleton<HandlerRegistration>(\"{handler.MessageTypeName}\",");
            source.AppendLine($"            new HandlerRegistration(");
            source.AppendLine($"                \"{handler.MessageTypeName}\",");

            if (isEffectivelyAsync)
            {
                source.AppendLine($"                {wrapperClassName}.UntypedHandleAsync,");
                source.AppendLine("                null,");
            }
            else
            {
                source.AppendLine($"                (mediator, message, cancellationToken, responseType) => new ValueTask<object?>({wrapperClassName}.UntypedHandle(mediator, message, cancellationToken, responseType)),");
                source.AppendLine($"                {wrapperClassName}.UntypedHandle,");
            }

            source.AppendLine($"                {isEffectivelyAsync.ToString().ToLower()}));");
        }

        source.AppendLine();
        source.AppendLine("        return services;");
        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }

    private static bool IsHandlerEffectivelyAsync(HandlerInfo handler, List<MiddlewareInfo> middlewares)
    {
        if (handler.IsAsync)
            return true;

        var applicableMiddlewares = GetApplicableMiddlewares(middlewares, handler);
        return applicableMiddlewares.Any(m => m.IsAsync);
    }

    private static List<MiddlewareInfo> GetApplicableMiddlewares(List<MiddlewareInfo> middlewares, HandlerInfo handler)
    {
        var applicable = new List<MiddlewareInfo>();

        foreach (var middleware in middlewares)
        {
            if (IsMiddlewareApplicableToHandler(middleware, handler))
            {
                applicable.Add(middleware);
            }
        }

        return applicable
            .OrderBy(m => m.Order)
            .ThenBy(m => m.IsObjectType ? 2 : (m.IsInterfaceType ? 1 : 0)) // Priority: specific=0, interface=1, object=2
            .ToList();
    }

    private static bool IsMiddlewareApplicableToHandler(MiddlewareInfo middleware, HandlerInfo handler)
    {
        if (middleware.IsObjectType)
            return true;

        if (middleware.MessageTypeName == handler.MessageTypeName)
            return true;

        if (middleware.IsInterfaceType && middleware.InterfaceTypes.Contains(handler.MessageTypeName))
            return true;

        return false;
    }
}
