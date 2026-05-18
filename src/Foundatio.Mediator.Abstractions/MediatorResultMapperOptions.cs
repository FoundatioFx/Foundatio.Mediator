namespace Foundatio.Mediator;

/// <summary>
/// Configures status-specific mappings used by the generated default mediator result mapper.
/// </summary>
/// <typeparam name="TResult">The transport-specific result type.</typeparam>
public sealed class MediatorResultMapperOptions<TResult>
{
    private readonly Dictionary<ResultStatus, Func<IResult, TResult>> _statusMappers = new();

    /// <summary>
    /// Adds or replaces the mapping used for a result status.
    /// </summary>
    /// <param name="status">The result status to map.</param>
    /// <param name="mapper">The mapping delegate.</param>
    /// <returns>The current options instance.</returns>
    public MediatorResultMapperOptions<TResult> MapStatus(ResultStatus status, Func<IResult, TResult> mapper)
    {
        if (mapper is null)
            throw new ArgumentNullException(nameof(mapper));

        _statusMappers[status] = mapper;
        return this;
    }

    /// <summary>
    /// Attempts to map a mediator result using a configured status-specific mapper.
    /// </summary>
    /// <param name="result">The mediator result to map.</param>
    /// <param name="mappedResult">The mapped result when a matching mapper is configured.</param>
    /// <returns><c>true</c> when a matching mapper was configured; otherwise <c>false</c>.</returns>
    public bool TryMap(IResult result, out TResult mappedResult)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        if (_statusMappers.TryGetValue(result.Status, out var mapper))
        {
            mappedResult = mapper(result);
            return true;
        }

        mappedResult = default!;
        return false;
    }
}