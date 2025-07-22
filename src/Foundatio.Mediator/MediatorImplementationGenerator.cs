using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Foundatio.Mediator;

internal static class MediatorImplementationGenerator
{
    public static string GenerateMediatorImplementation(List<HandlerInfo> handlers)
    {
        var source = new StringBuilder();

        source.AppendLine("#nullable enable");
        source.AppendLine("using System;");
        source.AppendLine("using System.Collections.Generic;");
        source.AppendLine("using System.Linq;");
        source.AppendLine("using System.Threading;");
        source.AppendLine("using System.Threading.Tasks;");
        source.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        source.AppendLine();
        source.AppendLine("namespace Foundatio.Mediator");
        source.AppendLine("{");
        source.AppendLine("    public class Mediator : IMediator");
        source.AppendLine("    {");
        source.AppendLine("        private readonly IServiceProvider _serviceProvider;");
        source.AppendLine();

        // Generate constructor
        source.AppendLine("        public Mediator(IServiceProvider serviceProvider)");
        source.AppendLine("        {");
        source.AppendLine("            _serviceProvider = serviceProvider;");
        source.AppendLine("        }");
        source.AppendLine();

        // Expose ServiceProvider for the generated static methods
        source.AppendLine("        public IServiceProvider ServiceProvider => _serviceProvider;");
        source.AppendLine();

        // Helper method to get all applicable handlers for a message type
        source.AppendLine("        private IEnumerable<HandlerRegistration> GetAllApplicableHandlers(object message)");
        source.AppendLine("        {");
        source.AppendLine("            var messageType = message.GetType();");
        source.AppendLine("            var allHandlers = new List<HandlerRegistration>();");
        source.AppendLine();
        source.AppendLine("            // Add handlers for the exact message type");
        source.AppendLine("            var exactHandlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(messageType.FullName);");
        source.AppendLine("            allHandlers.AddRange(exactHandlers);");
        source.AppendLine();
        source.AppendLine("            // Add handlers for all implemented interfaces");
        source.AppendLine("            foreach (var interfaceType in messageType.GetInterfaces())");
        source.AppendLine("            {");
        source.AppendLine("                var interfaceHandlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(interfaceType.FullName);");
        source.AppendLine("                allHandlers.AddRange(interfaceHandlers);");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            // Add handlers for all base classes");
        source.AppendLine("            var currentType = messageType.BaseType;");
        source.AppendLine("            while (currentType != null && currentType != typeof(object))");
        source.AppendLine("            {");
        source.AppendLine("                var baseHandlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(currentType.FullName);");
        source.AppendLine("                allHandlers.AddRange(baseHandlers);");
        source.AppendLine("                currentType = currentType.BaseType;");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            return allHandlers.Distinct();");
        source.AppendLine("        }");
        source.AppendLine();

        // Generate InvokeAsync method
        source.AppendLine("        public async ValueTask InvokeAsync(object message, CancellationToken cancellationToken = default)");
        source.AppendLine("        {");
        source.AppendLine("            var messageTypeName = message.GetType().FullName;");
        source.AppendLine("            var handlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(messageTypeName);");
        source.AppendLine("            var handlersList = handlers.ToList();");
        source.AppendLine();
        source.AppendLine("            if (handlersList.Count == 0)");
        source.AppendLine("                throw new InvalidOperationException($\"No handler found for message type {messageTypeName}\");");
        source.AppendLine();
        source.AppendLine("            if (handlersList.Count > 1)");
        source.AppendLine("                throw new InvalidOperationException($\"Multiple handlers found for message type {messageTypeName}. Use PublishAsync for multiple handlers.\");");
        source.AppendLine();
        source.AppendLine("            var handler = handlersList.First();");
        source.AppendLine("            await handler.HandleAsync(this, message, cancellationToken, null);");
        source.AppendLine("        }");
        source.AppendLine();

        // Generate Invoke method (sync)
        source.AppendLine("        public void Invoke(object message, CancellationToken cancellationToken = default)");
        source.AppendLine("        {");
        source.AppendLine("            var messageTypeName = message.GetType().FullName;");
        source.AppendLine("            var handlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(messageTypeName);");
        source.AppendLine("            var handlersList = handlers.ToList();");
        source.AppendLine();
        source.AppendLine("            if (handlersList.Count == 0)");
        source.AppendLine("                throw new InvalidOperationException($\"No handler found for message type {messageTypeName}\");");
        source.AppendLine();
        source.AppendLine("            if (handlersList.Count > 1)");
        source.AppendLine("                throw new InvalidOperationException($\"Multiple handlers found for message type {messageTypeName}. Use Publish for multiple handlers.\");");
        source.AppendLine();
        source.AppendLine("            var handler = handlersList.First();");
        source.AppendLine("            if (handler.IsAsync)");
        source.AppendLine("                throw new InvalidOperationException($\"Cannot use synchronous Invoke with async-only handler for message type {messageTypeName}. Use InvokeAsync instead.\");");
        source.AppendLine();
        source.AppendLine("            handler.Handle!(this, message, cancellationToken, null);");
        source.AppendLine("        }");
        source.AppendLine();

        // Generate InvokeAsync<TResponse> method
        source.AppendLine("        public async ValueTask<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default)");
        source.AppendLine("        {");
        source.AppendLine("            var messageTypeName = message.GetType().FullName;");
        source.AppendLine("            var handlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(messageTypeName);");
        source.AppendLine("            var handlersList = handlers.ToList();");
        source.AppendLine();
        source.AppendLine("            if (handlersList.Count == 0)");
        source.AppendLine("                throw new InvalidOperationException($\"No handler found for message type {messageTypeName}\");");
        source.AppendLine();
        source.AppendLine("            if (handlersList.Count > 1)");
        source.AppendLine("                throw new InvalidOperationException($\"Multiple handlers found for message type {messageTypeName}. Use PublishAsync for multiple handlers.\");");
        source.AppendLine();
        source.AppendLine("            var handler = handlersList.First();");
        source.AppendLine("            var result = await handler.HandleAsync(this, message, cancellationToken, typeof(TResponse));");
        source.AppendLine();
        source.AppendLine("            return (TResponse)result;");
        source.AppendLine("        }");
        source.AppendLine();

        // Generate Invoke<TResponse> method (sync)
        source.AppendLine("        public TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default)");
        source.AppendLine("        {");
        source.AppendLine("            var messageTypeName = message.GetType().FullName;");
        source.AppendLine("            var handlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(messageTypeName);");
        source.AppendLine("            var handlersList = handlers.ToList();");
        source.AppendLine();
        source.AppendLine("            if (handlersList.Count == 0)");
        source.AppendLine("                throw new InvalidOperationException($\"No handler found for message type {messageTypeName}\");");
        source.AppendLine();
        source.AppendLine("            if (handlersList.Count > 1)");
        source.AppendLine("                throw new InvalidOperationException($\"Multiple handlers found for message type {messageTypeName}. Use Publish for multiple handlers.\");");
        source.AppendLine();
        source.AppendLine("            var handler = handlersList.First();");
        source.AppendLine("            if (handler.IsAsync)");
        source.AppendLine("                throw new InvalidOperationException($\"Cannot use synchronous Invoke with async-only handler for message type {messageTypeName}. Use InvokeAsync instead.\");");
        source.AppendLine();
        source.AppendLine("            object result = handler.Handle!(this, message, cancellationToken, typeof(TResponse));");
        source.AppendLine("            return (TResponse)result;");
        source.AppendLine("        }");
        source.AppendLine();

        // Generate PublishAsync method
        source.AppendLine("        public async ValueTask PublishAsync(object message, CancellationToken cancellationToken = default)");
        source.AppendLine("        {");
        source.AppendLine("            var handlersList = GetAllApplicableHandlers(message).ToList();");
        source.AppendLine();
        source.AppendLine("            // Execute all handlers (zero to many allowed)");
        source.AppendLine("            var tasks = handlersList.Select(h => h.HandleAsync(this, message, cancellationToken, null));");
        source.AppendLine("            await Task.WhenAll(tasks.Select(t => t.AsTask()));");
        source.AppendLine("        }");
        source.AppendLine();

        // Generate Publish method (sync)
        source.AppendLine("        public void Publish(object message, CancellationToken cancellationToken = default)");
        source.AppendLine("        {");
        source.AppendLine("            var handlersList = GetAllApplicableHandlers(message).ToList();");
        source.AppendLine();
        source.AppendLine("            // Check if any handlers require async execution");
        source.AppendLine("            if (handlersList.Any(h => h.IsAsync))");
        source.AppendLine("            {");
        source.AppendLine("                var messageTypeName = message.GetType().FullName;");
        source.AppendLine("                throw new InvalidOperationException($\"Cannot use synchronous Publish with async-only handlers for message type {messageTypeName}. Use PublishAsync instead.\");");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            // Execute all handlers synchronously");
        source.AppendLine("            foreach (var handler in handlersList)");
        source.AppendLine("            {");
        source.AppendLine("                handler.Handle!(this, message, cancellationToken, null);");
        source.AppendLine("            }");
        source.AppendLine("        }");

        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }
}
