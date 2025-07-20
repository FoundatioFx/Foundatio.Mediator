#nullable enable
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

        public IServiceProvider ServiceProvider => _serviceProvider;

        public async ValueTask InvokeAsync(object message, CancellationToken cancellationToken = default)
        {
            var messageTypeName = message.GetType().FullName;
            var handlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(messageTypeName);
            var handlersList = handlers.ToList();

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {messageTypeName}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {messageTypeName}. Use PublishAsync for multiple handlers.");

            var handler = handlersList.First();
            await handler.HandleAsync(this, message, cancellationToken, null);
        }

        public void Invoke(object message, CancellationToken cancellationToken = default)
        {
            var messageTypeName = message.GetType().FullName;
            var handlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(messageTypeName);
            var handlersList = handlers.ToList();

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {messageTypeName}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {messageTypeName}. Use Publish for multiple handlers.");

            var handler = handlersList.First();
            if (handler.IsAsync)
                throw new InvalidOperationException($"Cannot use synchronous Invoke with async-only handler for message type {messageTypeName}. Use InvokeAsync instead.");

            handler.Handle!(this, message, cancellationToken, null);
        }

        public async ValueTask<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default)
        {
            var messageTypeName = message.GetType().FullName;
            var handlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(messageTypeName);
            var handlersList = handlers.ToList();

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {messageTypeName}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {messageTypeName}. Use PublishAsync for multiple handlers.");

            var handler = handlersList.First();
            var result = await handler.HandleAsync(this, message, cancellationToken, typeof(TResponse));

            return (TResponse)result;
        }

        public TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default)
        {
            var messageTypeName = message.GetType().FullName;
            var handlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(messageTypeName);
            var handlersList = handlers.ToList();

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {messageTypeName}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {messageTypeName}. Use Publish for multiple handlers.");

            var handler = handlersList.First();
            if (handler.IsAsync)
                throw new InvalidOperationException($"Cannot use synchronous Invoke with async-only handler for message type {messageTypeName}. Use InvokeAsync instead.");

            object result = handler.Handle!(this, message, cancellationToken, typeof(TResponse));
            return (TResponse)result;
        }

        public async ValueTask PublishAsync(object message, CancellationToken cancellationToken = default)
        {
            var messageTypeName = message.GetType().FullName;
            var handlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(messageTypeName);
            var handlersList = handlers.ToList();

            // Execute all handlers (zero to many allowed)
            var tasks = handlersList.Select(h => h.HandleAsync(this, message, cancellationToken, null));
            await Task.WhenAll(tasks.Select(t => t.AsTask()));
        }

        public void Publish(object message, CancellationToken cancellationToken = default)
        {
            var messageTypeName = message.GetType().FullName;
            var handlers = _serviceProvider.GetKeyedServices<HandlerRegistration>(messageTypeName);
            var handlersList = handlers.ToList();

            // Check if any handlers require async execution
            if (handlersList.Any(h => h.IsAsync))
                throw new InvalidOperationException($"Cannot use synchronous Publish with async-only handlers for message type {messageTypeName}. Use PublishAsync instead.");

            // Execute all handlers synchronously
            foreach (var handler in handlersList)
            {
                handler.Handle!(this, message, cancellationToken, null);
            }
        }
    }
}
