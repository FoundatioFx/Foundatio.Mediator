using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Foundatio.Mediator;

/// <summary>
/// A pooled, lightweight context for passing additional parameters to handler and middleware methods
/// beyond what is available in DI. Commonly used by endpoint generators to provide HttpContext
/// and other request-scoped values directly to handler parameters.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="Rent"/> to obtain an instance from the pool and <see cref="Dispose"/> to return it.
/// The typical usage pattern is:
/// </para>
/// <code>
/// using var ctx = CallContext.Rent().Set(httpContext);
/// await Handler.HandleAsync(mediator, message, ctx, cancellationToken);
/// </code>
/// <para>
/// Generated code checks <c>CallContext</c> before falling back to DI for parameter resolution,
/// so the cost when <c>callContext</c> is <c>null</c> is a single null check.
/// </para>
/// </remarks>
public sealed class CallContext : IDisposable
{
    private const int MaxPoolSize = 256;
    private static readonly ConcurrentQueue<CallContext> Pool = new();
    private static int _poolSize;

    private Dictionary<Type, object>? _items;

    private CallContext() { }

    /// <summary>
    /// Obtains a <see cref="CallContext"/> instance from the pool, or creates a new one if the pool is empty.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CallContext Rent()
    {
        if (Pool.TryDequeue(out var context))
        {
            Interlocked.Decrement(ref _poolSize);
            return context;
        }

        return new CallContext();
    }

    /// <summary>
    /// Sets a value by its type. Returns <c>this</c> for fluent chaining.
    /// </summary>
    /// <typeparam name="T">The type used as the lookup key.</typeparam>
    /// <param name="value">The value to store.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CallContext Set<T>(T value) where T : class
    {
        (_items ??= new Dictionary<Type, object>())[typeof(T)] = value;
        return this;
    }

    /// <summary>
    /// Sets a value by an explicit <see cref="Type"/> key. Returns <c>this</c> for fluent chaining.
    /// </summary>
    /// <param name="type">The type key.</param>
    /// <param name="value">The value to store.</param>
    public CallContext Set(Type type, object value)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (value is null) throw new ArgumentNullException(nameof(value));
        (_items ??= new Dictionary<Type, object>())[type] = value;
        return this;
    }

    /// <summary>
    /// Gets a value by type. Returns <c>null</c> when the type is not present.
    /// This method is called by generated code on the hot path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? Get(Type type)
    {
        if (_items is null)
            return null;

        _items.TryGetValue(type, out var value);
        return value;
    }

    /// <summary>
    /// Tries to get a strongly-typed value from the context.
    /// </summary>
    /// <typeparam name="T">The type used as the lookup key.</typeparam>
    /// <param name="value">The value if found; otherwise <c>default</c>.</param>
    /// <returns><c>true</c> if the value was found and is of the correct type; otherwise <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet<T>(out T? value) where T : class
    {
        if (_items is not null && _items.TryGetValue(typeof(T), out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Clears stored values and returns this instance to the pool.
    /// </summary>
    public void Dispose()
    {
        _items?.Clear();

        // Return to pool if under capacity
        if (Interlocked.Increment(ref _poolSize) <= MaxPoolSize)
        {
            Pool.Enqueue(this);
        }
        else
        {
            Interlocked.Decrement(ref _poolSize);
        }
    }
}
