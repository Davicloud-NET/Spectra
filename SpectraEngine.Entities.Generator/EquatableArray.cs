using System;
using System.Collections;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Generator;

// Array that compares by value, in order. ImmutableArray<T> compares by
// reference, which would make every model look changed on every run.
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items;

    public EquatableArray(T[]? items) => _items = items;

    public static EquatableArray<T> Empty => new(Array.Empty<T>());

    public int Count => _items is null ? 0 : _items.Length;

    public T this[int index] => _items![index];

    public bool Equals(EquatableArray<T> other)
    {
        T[]? left = _items;
        T[]? right = other._items;

        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Length != right.Length)
            return false;

        for (int i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        // Hand-rolled: System.HashCode is not in netstandard2.0.
        unchecked
        {
            int hash = 17;
            if (_items is not null)
            {
                for (int i = 0; i < _items.Length; i++)
                    hash = (hash * 31) + _items[i].GetHashCode();
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        T[] items = _items ?? Array.Empty<T>();
        for (int i = 0; i < items.Length; i++)
            yield return items[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class EquatableArray
{
    public static EquatableArray<T> From<T>(List<T> items)
        where T : IEquatable<T> => new(items.ToArray());
}
