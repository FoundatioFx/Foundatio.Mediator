namespace Foundatio.Mediator;

/// <summary>
/// Maps a mediator <see cref="IResult"/> to a transport-specific result object.
/// </summary>
/// <typeparam name="TResult">
/// The transport-specific result type (e.g. <c>Microsoft.AspNetCore.Http.IResult</c> for HTTP endpoints).
/// </typeparam>
/// <remarks>
/// <para>
/// For HTTP endpoints, the source generator registers a default
/// <c>IMediatorResultMapper&lt;Microsoft.AspNetCore.Http.IResult&gt;</c> implementation
/// that maps every <see cref="ResultStatus"/> to the corresponding ASP.NET Core
/// <c>Results.*</c> helper (e.g. <see cref="ResultStatus.NotFound"/> → <c>Results.NotFound()</c>).
/// </para>
/// <para>
/// To customize the mapping, register your own implementation <b>before</b> calling
/// <see cref="MediatorExtensions.AddMediator(Microsoft.Extensions.DependencyInjection.IServiceCollection, MediatorOptions?)"/>:
/// </para>
/// <code>
/// services.AddSingleton&lt;IMediatorResultMapper&lt;IResult&gt;, MyResultMapper&gt;();
/// services.AddMediator();
/// </code>
/// </remarks>
public interface IMediatorResultMapper<out TResult>
{
    /// <summary>
    /// Converts a mediator <see cref="IResult"/> to a transport-specific result.
    /// </summary>
    /// <param name="result">The mediator result to convert.</param>
    /// <returns>A <typeparamref name="TResult"/> instance.</returns>
    TResult MapResult(IResult result);
}
