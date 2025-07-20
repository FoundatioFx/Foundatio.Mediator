namespace Foundatio.Mediator
{
    /// <summary>
    /// Interface for handling messages of type TMessage with a return value of type TResponse
    /// </summary>
    /// <typeparam name="TMessage">The type of message to handle</typeparam>
    public interface IHandler<in TMessage>
    {
        /// <summary>
        /// Handles the specified message asynchronously and returns a response
        /// </summary>
        /// <param name="message">The message to handle</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>A ValueTask representing the asynchronous operation with a response</returns>
        ValueTask<TResponse> HandleAsync<TResponse>(TMessage message, CancellationToken cancellationToken);
    }
}
