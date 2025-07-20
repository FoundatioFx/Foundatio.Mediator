using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    public class Mediator : IMediator
    {
        private readonly IServiceProvider _serviceProvider;

        public Mediator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async ValueTask InvokeAsync(object message, CancellationToken cancellationToken = default)
        {
            var messageType = message.GetType();
            var registrationType = typeof(HandlerRegistration<>).MakeGenericType(messageType);
            var registrations = _serviceProvider.GetServices(registrationType).Cast<object>().ToList();

            if (registrations.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {messageType.Name}");

            if (registrations.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {messageType.Name}. Use PublishAsync for multiple handlers.");

            var registration = registrations[0];
            var handlerProperty = registrationType.GetProperty("Handler");
            var handler = handlerProperty!.GetValue(registration);
            var handlerType = typeof(IHandler<>).MakeGenericType(messageType);

            // Call the generic HandleAsync<object> method and ignore the result
            var handleAsyncMethod = handlerType.GetMethod("HandleAsync");
            var genericMethod = handleAsyncMethod!.MakeGenericMethod(typeof(object));
            var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });
            await (ValueTask<object>)result!;
        }

        public void Invoke(object message, CancellationToken cancellationToken = default)
        {
            var messageType = message.GetType();
            var registrationType = typeof(HandlerRegistration<>).MakeGenericType(messageType);
            var registrations = _serviceProvider.GetServices(registrationType).Cast<object>().ToList();

            if (registrations.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {messageType.Name}");

            if (registrations.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {messageType.Name}. Use PublishAsync for multiple handlers.");

            var registration = registrations[0];
            var handlerProperty = registrationType.GetProperty("Handler");
            var handler = handlerProperty!.GetValue(registration);
            var handlerType = typeof(IHandler<>).MakeGenericType(messageType);

            // Call the generic HandleAsync<object> method and ignore the result
            var handleAsyncMethod = handlerType.GetMethod("HandleAsync");
            var genericMethod = handleAsyncMethod!.MakeGenericMethod(typeof(object));
            var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });
            ((ValueTask<object>)result!).GetAwaiter().GetResult();
        }

        public async ValueTask<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default)
        {
            var messageType = message.GetType();
            var registrationType = typeof(HandlerRegistration<>).MakeGenericType(messageType);
            var registrations = _serviceProvider.GetServices(registrationType).Cast<object>().ToList();

            if (registrations.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {messageType.Name}");

            if (registrations.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {messageType.Name}. Use PublishAsync for multiple handlers.");

            var registration = registrations[0];
            var handlerProperty = registrationType.GetProperty("Handler");
            var handler = handlerProperty!.GetValue(registration);
            var handlerType = typeof(IHandler<>).MakeGenericType(messageType);

            // Call the generic HandleAsync<TResponse> method
            var handleAsyncMethod = handlerType.GetMethod("HandleAsync");
            var genericMethod = handleAsyncMethod!.MakeGenericMethod(typeof(TResponse));
            var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });
            return await (ValueTask<TResponse>)result!;
        }

        public TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default)
        {
            var messageType = message.GetType();
            var registrationType = typeof(HandlerRegistration<>).MakeGenericType(messageType);
            var registrations = _serviceProvider.GetServices(registrationType).Cast<object>().ToList();

            if (registrations.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {messageType.Name}");

            if (registrations.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {messageType.Name}. Use PublishAsync for multiple handlers.");

            var registration = registrations[0];
            var handlerProperty = registrationType.GetProperty("Handler");
            var handler = handlerProperty!.GetValue(registration);
            var handlerType = typeof(IHandler<>).MakeGenericType(messageType);

            // Call the generic HandleAsync<TResponse> method synchronously
            var handleAsyncMethod = handlerType.GetMethod("HandleAsync");
            var genericMethod = handleAsyncMethod!.MakeGenericMethod(typeof(TResponse));
            var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });
            return ((ValueTask<TResponse>)result!).GetAwaiter().GetResult();
        }

        public async ValueTask PublishAsync(object message, CancellationToken cancellationToken = default)
        {
            var messageType = message.GetType();
            var registrationType = typeof(HandlerRegistration<>).MakeGenericType(messageType);
            var registrations = _serviceProvider.GetServices(registrationType).Cast<object>().ToList();

            if (registrations.Count == 0)
                return; // No handlers, no-op

            var handlerProperty = registrationType.GetProperty("Handler");
            var handlerType = typeof(IHandler<>).MakeGenericType(messageType);
            var handleAsyncMethod = handlerType.GetMethod("HandleAsync");
            var genericMethod = handleAsyncMethod!.MakeGenericMethod(typeof(object));

            if (registrations.Count == 1)
            {
                // Single handler - direct call
                var handler = handlerProperty!.GetValue(registrations[0]);
                var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });
                await (ValueTask<object>)result!;
            }
            else
            {
                // Multiple handlers - call all sequentially with error handling
                var tasks = new List<Task>();
                Exception? firstException = null;

                foreach (var registration in registrations)
                {
                    try
                    {
                        var handler = handlerProperty!.GetValue(registration);
                        var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });
                        tasks.Add(((ValueTask<object>)result!).AsTask());
                    }
                    catch (Exception ex)
                    {
                        firstException ??= ex;
                    }
                }

                // Wait for all async tasks to complete
                if (tasks.Count > 0)
                {
                    try
                    {
                        await Task.WhenAll(tasks);
                    }
                    catch (Exception ex)
                    {
                        firstException ??= ex;
                    }
                }

                // Re-throw the first exception if any occurred
                if (firstException != null)
                    throw firstException;
            }
        }

        public void Publish(object message, CancellationToken cancellationToken = default)
        {
            var messageType = message.GetType();
            var registrationType = typeof(HandlerRegistration<>).MakeGenericType(messageType);
            var registrations = _serviceProvider.GetServices(registrationType).Cast<object>().ToList();

            if (registrations.Count == 0)
                return; // No handlers, no-op

            var handlerProperty = registrationType.GetProperty("Handler");
            var handlerType = typeof(IHandler<>).MakeGenericType(messageType);
            var handleAsyncMethod = handlerType.GetMethod("HandleAsync");
            var genericMethod = handleAsyncMethod!.MakeGenericMethod(typeof(object));

            Exception? firstException = null;

            foreach (var registration in registrations)
            {
                try
                {
                    var handler = handlerProperty!.GetValue(registration);
                    var result = genericMethod.Invoke(handler, new[] { message, cancellationToken });
                    ((ValueTask<object>)result!).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    firstException ??= ex;
                }
            }

            // Re-throw the first exception if any occurred
            if (firstException != null)
                throw firstException;
        }
    }
}
