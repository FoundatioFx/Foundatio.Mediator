namespace Foundatio.Mediator;

/// <summary>
/// Enriches an endpoint-bound message with data from the transport context before it is dispatched
/// to its handler (e.g. stamping tenant or user information from the HTTP request onto the message).
/// </summary>
/// <typeparam name="TContext">
/// The transport-specific context type (e.g. <c>Microsoft.AspNetCore.Http.HttpContext</c> for HTTP endpoints).
/// </typeparam>
/// <remarks>
/// <para>
/// Generated HTTP endpoints resolve all registered
/// <c>IMediatorMessageEnricher&lt;Microsoft.AspNetCore.Http.HttpContext&gt;</c> implementations and
/// invoke them in registration order after the message is bound and before the handler is invoked.
/// Enrichers are resolved once when endpoints are mapped, so they must be registered as singletons;
/// scoped services can be reached through the context (e.g. <c>HttpContext.RequestServices</c>).
/// </para>
/// <code>
/// services.AddSingleton&lt;IMediatorMessageEnricher&lt;HttpContext&gt;, TenantMessageEnricher&gt;();
/// </code>
/// <para>
/// Because messages are typically immutable records, an enricher returns the message to dispatch:
/// either the original instance or a modified copy (e.g. a <c>with</c> expression). The returned
/// instance must be of the same type as the bound message.
/// </para>
/// </remarks>
public interface IMediatorMessageEnricher<in TContext>
{
    /// <summary>
    /// Enriches a bound message using the transport context.
    /// </summary>
    /// <param name="message">The bound message.</param>
    /// <param name="context">The transport-specific context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The message to dispatch: the original instance or an enriched copy of the same type.</returns>
    ValueTask<object> EnrichAsync(object message, TContext context, CancellationToken cancellationToken);
}
