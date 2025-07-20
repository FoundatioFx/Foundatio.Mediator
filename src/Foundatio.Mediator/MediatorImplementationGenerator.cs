using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Foundatio.Mediator;

internal static class MediatorImplementationGenerator
{
    public static string GenerateMediatorImplementation(List<HandlerToGenerate> handlers)
    {
        var source = new StringBuilder();
        
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
        source.AppendLine("        public Mediator(IServiceProvider serviceProvider)");
        source.AppendLine("        {");
        source.AppendLine("            _serviceProvider = serviceProvider;");
        source.AppendLine("        }");
        source.AppendLine();

        // Generate InvokeAsync method
        source.AppendLine("        public async ValueTask InvokeAsync(object message, CancellationToken cancellationToken = default)");
        source.AppendLine("        {");
        source.AppendLine("            var messageType = message.GetType();");
        source.AppendLine("            var registrationType = typeof(HandlerRegistration<>).MakeGenericType(messageType);");
        source.AppendLine("            var registrations = _serviceProvider.GetServices(registrationType).Cast<object>().ToList();");
        source.AppendLine();
        source.AppendLine("            if (registrations.Count == 0)");
        source.AppendLine("                throw new InvalidOperationException($\"No handler found for message type {messageType.Name}\");");
        source.AppendLine();
        source.AppendLine("            if (registrations.Count > 1)");
        source.AppendLine("                throw new InvalidOperationException($\"Multiple handlers found for message type {messageType.Name}. Use PublishAsync for multiple handlers.\");");
        source.AppendLine();
        source.AppendLine("            var registration = registrations[0];");
        source.AppendLine("            var handlerProperty = registrationType.GetProperty(\"Handler\");");
        source.AppendLine("            var handler = handlerProperty!.GetValue(registration);");
        source.AppendLine("            var handlerType = typeof(IHandler<>).MakeGenericType(messageType);");
        source.AppendLine();
        source.AppendLine("            // Call the generic HandleAsync<object> method and ignore the result");
        source.AppendLine("            var handleAsyncMethod = handlerType.GetMethod(\"HandleAsync\");");
        source.AppendLine("            var genericMethod = handleAsyncMethod!.MakeGenericMethod(typeof(object));");
        source.AppendLine("            var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });");
        source.AppendLine("            await (ValueTask<object>)result!;");
        source.AppendLine("        }");
        source.AppendLine();

        // Generate Invoke method  
        source.AppendLine("        public void Invoke(object message, CancellationToken cancellationToken = default)");
        source.AppendLine("        {");
        source.AppendLine("            var messageType = message.GetType();");
        source.AppendLine("            var registrationType = typeof(HandlerRegistration<>).MakeGenericType(messageType);");
        source.AppendLine("            var registrations = _serviceProvider.GetServices(registrationType).Cast<object>().ToList();");
        source.AppendLine();
        source.AppendLine("            if (registrations.Count == 0)");
        source.AppendLine("                throw new InvalidOperationException($\"No handler found for message type {messageType.Name}\");");
        source.AppendLine();
        source.AppendLine("            if (registrations.Count > 1)");
        source.AppendLine("                throw new InvalidOperationException($\"Multiple handlers found for message type {messageType.Name}. Use PublishAsync for multiple handlers.\");");
        source.AppendLine();
        source.AppendLine("            var registration = registrations[0];");
        source.AppendLine("            var handlerProperty = registrationType.GetProperty(\"Handler\");");
        source.AppendLine("            var handler = handlerProperty!.GetValue(registration);");
        source.AppendLine("            var handlerType = typeof(IHandler<>).MakeGenericType(messageType);");
        source.AppendLine();
        source.AppendLine("            // Call the generic HandleAsync<object> method and ignore the result");
        source.AppendLine("            var handleAsyncMethod = handlerType.GetMethod(\"HandleAsync\");");
        source.AppendLine("            var genericMethod = handleAsyncMethod!.MakeGenericMethod(typeof(object));");
        source.AppendLine("            var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });");
        source.AppendLine("            ((ValueTask<object>)result!).GetAwaiter().GetResult();");
        source.AppendLine("        }");
        source.AppendLine();

        // Generate InvokeAsync<TResponse> method
        source.AppendLine("        public async ValueTask<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default)");
        source.AppendLine("        {");
        source.AppendLine("            var messageType = message.GetType();");
        source.AppendLine("            var registrationType = typeof(HandlerRegistration<>).MakeGenericType(messageType);");
        source.AppendLine("            var registrations = _serviceProvider.GetServices(registrationType).Cast<object>().ToList();");
        source.AppendLine();
        source.AppendLine("            if (registrations.Count == 0)");
        source.AppendLine("                throw new InvalidOperationException($\"No handler found for message type {messageType.Name}\");");
        source.AppendLine();
        source.AppendLine("            if (registrations.Count > 1)");
        source.AppendLine("                throw new InvalidOperationException($\"Multiple handlers found for message type {messageType.Name}. Use PublishAsync for multiple handlers.\");");
        source.AppendLine();
        source.AppendLine("            var registration = registrations[0];");
        source.AppendLine("            var handlerProperty = registrationType.GetProperty(\"Handler\");");
        source.AppendLine("            var handler = handlerProperty!.GetValue(registration);");
        source.AppendLine("            var handlerType = typeof(IHandler<>).MakeGenericType(messageType);");
        source.AppendLine();
        source.AppendLine("            // Call the generic HandleAsync<TResponse> method");
        source.AppendLine("            var handleAsyncMethod = handlerType.GetMethod(\"HandleAsync\");");
        source.AppendLine("            var genericMethod = handleAsyncMethod!.MakeGenericMethod(typeof(TResponse));");
        source.AppendLine("            var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });");
        source.AppendLine("            return await (ValueTask<TResponse>)result!;");
        source.AppendLine("        }");
        source.AppendLine();

        // Generate Invoke<TResponse> method
        source.AppendLine("        public TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default)");
        source.AppendLine("        {");
        source.AppendLine("            var messageType = message.GetType();");
        source.AppendLine("            var registrationType = typeof(HandlerRegistration<>).MakeGenericType(messageType);");
        source.AppendLine("            var registrations = _serviceProvider.GetServices(registrationType).Cast<object>().ToList();");
        source.AppendLine();
        source.AppendLine("            if (registrations.Count == 0)");
        source.AppendLine("                throw new InvalidOperationException($\"No handler found for message type {messageType.Name}\");");
        source.AppendLine();
        source.AppendLine("            if (registrations.Count > 1)");
        source.AppendLine("                throw new InvalidOperationException($\"Multiple handlers found for message type {messageType.Name}. Use PublishAsync for multiple handlers.\");");
        source.AppendLine();
        source.AppendLine("            var registration = registrations[0];");
        source.AppendLine("            var handlerProperty = registrationType.GetProperty(\"Handler\");");
        source.AppendLine("            var handler = handlerProperty!.GetValue(registration);");
        source.AppendLine("            var handlerType = typeof(IHandler<>).MakeGenericType(messageType);");
        source.AppendLine();
        source.AppendLine("            // Call the generic HandleAsync<TResponse> method synchronously");
        source.AppendLine("            var handleAsyncMethod = handlerType.GetMethod(\"HandleAsync\");");
        source.AppendLine("            var genericMethod = handleAsyncMethod!.MakeGenericMethod(typeof(TResponse));");
        source.AppendLine("            var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });");
        source.AppendLine("            return ((ValueTask<TResponse>)result!).GetAwaiter().GetResult();");
        source.AppendLine("        }");
        source.AppendLine();

        // Generate PublishAsync method
        source.AppendLine("        public async ValueTask PublishAsync(object message, CancellationToken cancellationToken = default)");
        source.AppendLine("        {");
        source.AppendLine("            var messageType = message.GetType();");
        source.AppendLine("            var registrationType = typeof(HandlerRegistration<>).MakeGenericType(messageType);");
        source.AppendLine("            var registrations = _serviceProvider.GetServices(registrationType).Cast<object>().ToList();");
        source.AppendLine();
        source.AppendLine("            if (registrations.Count == 0)");
        source.AppendLine("                return; // No handlers, no-op");
        source.AppendLine();
        source.AppendLine("            var handlerProperty = registrationType.GetProperty(\"Handler\");");
        source.AppendLine("            var handlerType = typeof(IHandler<>).MakeGenericType(messageType);");
        source.AppendLine("            var handleAsyncMethod = handlerType.GetMethod(\"HandleAsync\");");
        source.AppendLine("            var genericMethod = handleAsyncMethod!.MakeGenericMethod(typeof(object));");
        source.AppendLine();
        source.AppendLine("            if (registrations.Count == 1)");
        source.AppendLine("            {");
        source.AppendLine("                // Single handler - direct call");
        source.AppendLine("                var handler = handlerProperty!.GetValue(registrations[0]);");
        source.AppendLine("                var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });");
        source.AppendLine("                await (ValueTask<object>)result!;");
        source.AppendLine("            }");
        source.AppendLine("            else");
        source.AppendLine("            {");
        source.AppendLine("                // Multiple handlers - call all sequentially with error handling");
        source.AppendLine("                var tasks = new List<Task>();");
        source.AppendLine("                Exception? firstException = null;");
        source.AppendLine();
        source.AppendLine("                foreach (var registration in registrations)");
        source.AppendLine("                {");
        source.AppendLine("                    try");
        source.AppendLine("                    {");
        source.AppendLine("                        var handler = handlerProperty!.GetValue(registration);");
        source.AppendLine("                        var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });");
        source.AppendLine("                        tasks.Add(((ValueTask<object>)result!).AsTask());");
        source.AppendLine("                    }");
        source.AppendLine("                    catch (Exception ex)");
        source.AppendLine("                    {");
        source.AppendLine("                        firstException ??= ex;");
        source.AppendLine("                    }");
        source.AppendLine("                }");
        source.AppendLine();
        source.AppendLine("                // Wait for all async tasks to complete");
        source.AppendLine("                if (tasks.Count > 0)");
        source.AppendLine("                {");
        source.AppendLine("                    try");
        source.AppendLine("                    {");
        source.AppendLine("                        await Task.WhenAll(tasks);");
        source.AppendLine("                    }");
        source.AppendLine("                    catch (Exception ex)");
        source.AppendLine("                    {");
        source.AppendLine("                        firstException ??= ex;");
        source.AppendLine("                    }");
        source.AppendLine("                }");
        source.AppendLine();
        source.AppendLine("                // Re-throw the first exception if any occurred");
        source.AppendLine("                if (firstException != null)");
        source.AppendLine("                    throw firstException;");
        source.AppendLine("            }");
        source.AppendLine("        }");
        source.AppendLine();

        // Generate Publish method
        source.AppendLine("        public void Publish(object message, CancellationToken cancellationToken = default)");
        source.AppendLine("        {");
        source.AppendLine("            var messageType = message.GetType();");
        source.AppendLine("            var registrationType = typeof(HandlerRegistration<>).MakeGenericType(messageType);");
        source.AppendLine("            var registrations = _serviceProvider.GetServices(registrationType).Cast<object>().ToList();");
        source.AppendLine();
        source.AppendLine("            if (registrations.Count == 0)");
        source.AppendLine("                return; // No handlers, no-op");
        source.AppendLine();
        source.AppendLine("            var handlerProperty = registrationType.GetProperty(\"Handler\");");
        source.AppendLine("            var handlerType = typeof(IHandler<>).MakeGenericType(messageType);");
        source.AppendLine("            var handleAsyncMethod = handlerType.GetMethod(\"HandleAsync\");");
        source.AppendLine("            var genericMethod = handleAsyncMethod!.MakeGenericMethod(typeof(object));");
        source.AppendLine();
        source.AppendLine("            Exception? firstException = null;");
        source.AppendLine();
        source.AppendLine("            foreach (var registration in registrations)");
        source.AppendLine("            {");
        source.AppendLine("                try");
        source.AppendLine("                {");
        source.AppendLine("                    var handler = handlerProperty!.GetValue(registration);");
        source.AppendLine("                    var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });");
        source.AppendLine("                    ((ValueTask<object>)result!).GetAwaiter().GetResult();");
        source.AppendLine("                }");
        source.AppendLine("                catch (Exception ex)");
        source.AppendLine("                {");
        source.AppendLine("                    firstException ??= ex;");
        source.AppendLine("                }");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            // Re-throw the first exception if any occurred");
        source.AppendLine("            if (firstException != null)");
        source.AppendLine("                throw firstException;");
        source.AppendLine("        }");

        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }
}
