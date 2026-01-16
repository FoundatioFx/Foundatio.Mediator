namespace Foundatio.Mediator;

/// <summary>
/// Marker interface for queries in the Foundatio Mediator system.
/// </summary>
public interface IQuery { }

/// <summary>
/// Marker interface for queries that return a specific response type.
/// Inherits from <see cref="IRequest{TResponse}"/> to enable type inference when invoking.
/// </summary>
/// <typeparam name="TResponse">The type of response expected from the handler.</typeparam>
/// <example>
/// <code>
/// public record GetUser(int Id) : IQuery&lt;User&gt;;
///
/// // Type inference works - no need to specify User explicitly
/// var user = await mediator.InvokeAsync(new GetUser(123));
/// </code>
/// </example>
public interface IQuery<TResponse> : IQuery, IRequest<TResponse> { }
