namespace Foundatio.Mediator;

/// <summary>
/// Configures status-specific mappings used by the generated default mediator result mapper.
/// </summary>
/// <typeparam name="TResult">The transport-specific result type.</typeparam>
public sealed class MediatorResultMapperOptions<TResult>
{
    private readonly Dictionary<ResultStatus, Func<IResult, TResult>> _statusMappers = new();
    private readonly List<ValueMapper> _valueMappers = new();

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
    /// Adds a mapping for results whose value (<see cref="IResult.GetValue"/>) is assignable to
    /// <typeparamref name="TValue"/>. Value mappers are evaluated in registration order and take
    /// precedence over status mappers, enabling shape-based mapping (e.g. paged results, custom
    /// response envelopes) regardless of the result status.
    /// </summary>
    /// <typeparam name="TValue">The result value type (or interface/base type) to match.</typeparam>
    /// <param name="map">The mapping delegate, invoked with the strongly-typed value.</param>
    /// <returns>The current options instance.</returns>
    public MediatorResultMapperOptions<TResult> MapValue<TValue>(Func<TValue, TResult> map)
    {
        if (map is null)
            throw new ArgumentNullException(nameof(map));

        _valueMappers.Add(new ValueMapper(
            static result => result.GetValue() is TValue,
            result => map((TValue)result.GetValue()!)));
        return this;
    }

    /// <summary>
    /// Adds a conditional mapping for results whose value (<see cref="IResult.GetValue"/>) is
    /// assignable to <typeparamref name="TValue"/> and satisfies <paramref name="when"/>. Value
    /// mappers are evaluated in registration order and take precedence over status mappers.
    /// </summary>
    /// <typeparam name="TValue">The result value type (or interface/base type) to match.</typeparam>
    /// <param name="when">Predicate that must return <c>true</c> for the mapping to apply.</param>
    /// <param name="map">The mapping delegate, invoked with the strongly-typed value.</param>
    /// <returns>The current options instance.</returns>
    public MediatorResultMapperOptions<TResult> MapValue<TValue>(Func<TValue, bool> when, Func<TValue, TResult> map)
    {
        if (when is null)
            throw new ArgumentNullException(nameof(when));
        if (map is null)
            throw new ArgumentNullException(nameof(map));

        _valueMappers.Add(new ValueMapper(
            result => result.GetValue() is TValue value && when(value),
            result => map((TValue)result.GetValue()!)));
        return this;
    }

    /// <summary>
    /// Attempts to map a mediator result using a configured value mapper (evaluated first, in
    /// registration order) or status-specific mapper.
    /// </summary>
    /// <param name="result">The mediator result to map.</param>
    /// <param name="mappedResult">The mapped result when a matching mapper is configured.</param>
    /// <returns><c>true</c> when a matching mapper was configured; otherwise <c>false</c>.</returns>
    public bool TryMap(IResult result, out TResult mappedResult)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        foreach (var valueMapper in _valueMappers)
        {
            if (valueMapper.CanMap(result))
            {
                mappedResult = valueMapper.Map(result);
                return true;
            }
        }

        if (_statusMappers.TryGetValue(result.Status, out var mapper))
        {
            mappedResult = mapper(result);
            return true;
        }

        mappedResult = default!;
        return false;
    }

    private readonly struct ValueMapper(Func<IResult, bool> canMap, Func<IResult, TResult> map)
    {
        public Func<IResult, bool> CanMap { get; } = canMap;
        public Func<IResult, TResult> Map { get; } = map;
    }
}