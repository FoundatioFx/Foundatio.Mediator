using System.Collections;

namespace Foundatio.Mediator;

/// <summary>
/// An array that implements IEquatable for use in source generators
/// </summary>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(Array.Empty<T>());

    private readonly T[]? _array;

    public EquatableArray(T[] array)
    {
        _array = array;
    }

    public ReadOnlySpan<T> AsSpan() => _array.AsSpan();

    public bool Equals(EquatableArray<T> other)
    {
        return AsSpan().SequenceEqual(other.AsSpan());
    }

    public override bool Equals(object? obj)
    {
        return obj is EquatableArray<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (_array is null)
        {
            return 0;
        }

        unchecked
        {
            var hash = 17;
            foreach (var item in _array)
            {
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        return (_array ?? Array.Empty<T>()).AsEnumerable().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
    {
        return !left.Equals(right);
    }
}
