using System.Threading;
using System.Threading.Tasks;

namespace Foundatio.Mediator
{
    /// <summary>
    /// Registration information for a handler
    /// </summary>
    /// <typeparam name="TMessage">The type of message the handler processes</typeparam>
    public class HandlerRegistration<TMessage>
    {
        /// <summary>
        /// Creates a new handler registration
        /// </summary>
        /// <param name="handler">The handler instance</param>
        /// <param name="isAsync">Whether the handler supports async operations</param>
        public HandlerRegistration(IHandler<TMessage> handler, bool isAsync)
        {
            Handler = handler;
            IsAsync = isAsync;
        }

        /// <summary>
        /// The handler instance
        /// </summary>
        public IHandler<TMessage> Handler { get; }

        /// <summary>
        /// Whether the handler supports async operations
        /// </summary>
        public bool IsAsync { get; }
    }
}
