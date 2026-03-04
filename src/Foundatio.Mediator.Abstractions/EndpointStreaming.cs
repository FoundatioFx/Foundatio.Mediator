namespace Foundatio.Mediator;

/// <summary>
/// Specifies the streaming format for endpoints that return <c>IAsyncEnumerable&lt;T&gt;</c>.
/// </summary>
public enum EndpointStreaming
{
    /// <summary>
    /// Returns the <c>IAsyncEnumerable&lt;T&gt;</c> directly. ASP.NET Core streams the results
    /// as a JSON array, writing each element as it becomes available.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Wraps the <c>IAsyncEnumerable&lt;T&gt;</c> with <c>TypedResults.ServerSentEvents()</c>
    /// so the endpoint uses the <c>text/event-stream</c> content type. The framework handles
    /// all header and serialization concerns. Clients consume via the browser <c>EventSource</c> API.
    /// Requires .NET 10 or later in the consuming project.
    /// </summary>
    ServerSentEvents = 1
}
