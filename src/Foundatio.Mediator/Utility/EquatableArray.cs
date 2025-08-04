using System.Collections;

namespace Foundatio.Mediator.Utility;

/// <summary>
/// An array that implements IEquatable for use in source generators
/// </summary>
public readonly struct EquatableArray<T>(T[] array) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new([]);

    private readonly T[]? _array = array;

    public ReadOnlySpan<T> AsSpan() => _array.AsSpan();

    public int Length => _array?.Length ?? 0;

    public T this[int index]
    {
        get
        {
            if (_array == null)
                throw new IndexOutOfRangeException("Array is null or index is out of range.");

            if (index < 0 || index >= _array.Length)
                throw new IndexOutOfRangeException("Array index is out of range.");

            return _array[index];
        }
    }

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
            int hash = 17;
            foreach (var item in _array)
            {
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        return (_array ?? []).AsEnumerable().GetEnumerator();
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
