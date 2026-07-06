namespace Common.Module;

/// <summary>
/// Interface for messages that carry information about who initiated the request.
/// The <c>SetRequestedByMiddleware</c> automatically populates <see cref="RequestedBy"/>
/// from the HTTP context so that handlers receive the caller's identity without
/// coupling to ASP.NET Core.
/// </summary>
/// <remarks>
/// Messages are immutable records, and C# <c>with</c> expressions can't be used through an
/// interface — so implementers provide the one-line <see cref="WithRequestedBy"/> self-copy
/// that middleware uses to produce the enriched message.
/// </remarks>
public interface IHasRequestedBy
{
    /// <summary>
    /// Gets the identity of the user or system that initiated the request.
    /// Populated automatically by <c>SetRequestedByMiddleware</c> when the message
    /// is dispatched through a minimal API endpoint.
    /// </summary>
    string? RequestedBy { get; }

    /// <summary>
    /// Returns a copy of the message with <see cref="RequestedBy"/> set.
    /// Implement as <c>=&gt; this with { RequestedBy = requestedBy };</c>
    /// </summary>
    IHasRequestedBy WithRequestedBy(string requestedBy);
}
