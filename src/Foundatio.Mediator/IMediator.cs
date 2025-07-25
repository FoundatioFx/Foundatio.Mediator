namespace Foundatio.Mediator;

/// <summary>
/// A high-performance mediator for dispatching messages to handlers using convention-based discovery.
/// Supports both invoke (single handler) and publish (multiple handlers) patterns with minimal overhead.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Gets the service provider used for dependency injection.
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Asynchronously invokes exactly one handler for the specified message.
    /// </summary>
    /// <param name="message">The message to send to a handler.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no handler or multiple handlers are found for the message type.</exception>
    /// <remarks>
    /// If the handler returns a tuple, any additional values beyond the first will be automatically published as cascading messages.
    /// The operation will not complete until all cascading messages have been handled.
    /// </remarks>
    ValueTask InvokeAsync(object message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously invokes exactly one handler for the specified message and returns a response of the specified type.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="message">The message to send to a handler.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing the response of type <typeparamref name="TResponse"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no handler or multiple handlers are found for the message type.</exception>
    /// <exception cref="InvalidCastException">Thrown when the handler's return value cannot be cast to <typeparamref name="TResponse"/>.</exception>
    /// <remarks>
    /// If the handler returns a tuple, the mediator will extract the value matching <typeparamref name="TResponse"/> and
    /// automatically publish any remaining values as cascading messages. The operation will not complete until all
    /// cascading messages have been handled.
    /// </remarks>
    ValueTask<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously invokes exactly one handler for the specified message.
    /// </summary>
    /// <param name="message">The message to send to a handler.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when no handler or multiple handlers are found for the message type.</exception>
    /// <exception cref="InvalidOperationException">Thrown when attempting to synchronously invoke an async-only handler.</exception>
    /// <remarks>
    /// This method can only be used with handlers that have synchronous implementations. If the handler is async-only,
    /// use <see cref="InvokeAsync(object, CancellationToken)"/> instead. If the handler returns a tuple, any additional
    /// values beyond the first will be automatically published as cascading messages.
    /// </remarks>
    void Invoke(object message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously invokes exactly one handler for the specified message and returns a response of the specified type.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="message">The message to send to a handler.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The response of type <typeparamref name="TResponse"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no handler or multiple handlers are found for the message type.</exception>
    /// <exception cref="InvalidOperationException">Thrown when attempting to synchronously invoke an async-only handler.</exception>
    /// <exception cref="InvalidCastException">Thrown when the handler's return value cannot be cast to <typeparamref name="TResponse"/>.</exception>
    /// <remarks>
    /// This method can only be used with handlers that have synchronous implementations. If the handler is async-only,
    /// use <see cref="InvokeAsync{TResponse}(object, CancellationToken)"/> instead. If the handler returns a tuple,
    /// the mediator will extract the value matching <typeparamref name="TResponse"/> and automatically publish any
    /// remaining values as cascading messages.
    /// </remarks>
    TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously publishes a message to zero or more handlers.
    /// </summary>
    /// <param name="message">The message to publish to handlers.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// All handlers for the message type will be executed. The execution mode (sequential or parallel) is determined
    /// by the mediator configuration. If any handler throws an exception, all other handlers will still execute,
    /// and the first exception encountered will be thrown after all handlers complete.
    /// </remarks>
    ValueTask PublishAsync(object message, CancellationToken cancellationToken = default);
}
