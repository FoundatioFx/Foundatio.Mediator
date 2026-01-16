namespace Foundatio.Mediator;

/// <summary>
/// Marker interface for requests that return a specific response type.
/// Implementing this interface enables type inference when calling <see cref="IMediator.InvokeAsync{TResponse}(IRequest{TResponse}, CancellationToken)"/>.
/// </summary>
/// <typeparam name="TResponse">The type of response expected from the handler.</typeparam>
/// <example>
/// <code>
/// public record GetUser(int Id) : IRequest&lt;User&gt;;
///
/// // Type inference works - no need to specify User explicitly
/// var user = await mediator.InvokeAsync(new GetUser(123));
/// </code>
/// </example>
public interface IRequest<TResponse> { }
