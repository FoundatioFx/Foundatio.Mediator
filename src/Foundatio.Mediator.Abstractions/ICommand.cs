namespace Foundatio.Mediator;

/// <summary>
/// Marker interface for commands in the Foundatio Mediator system.
/// </summary>
public interface ICommand { }

/// <summary>
/// Marker interface for commands that return a specific response type.
/// Inherits from <see cref="IRequest{TResponse}"/> to enable type inference when invoking.
/// </summary>
/// <typeparam name="TResponse">The type of response expected from the handler.</typeparam>
/// <example>
/// <code>
/// public record CreateUser(string Name, string Email) : ICommand&lt;User&gt;;
///
/// // Type inference works - no need to specify User explicitly
/// var user = await mediator.InvokeAsync(new CreateUser("John", "john@example.com"));
/// </code>
/// </example>
public interface ICommand<TResponse> : ICommand, IRequest<TResponse> { }
