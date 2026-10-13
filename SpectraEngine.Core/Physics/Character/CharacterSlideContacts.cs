using System;
using System.Runtime.CompilerServices;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// The nodes the slide of one tick stopped at, by id, in the order it met
/// them. It keeps the first <see cref="Capacity"/>.
/// </summary>
// No references, so CharacterState stays copyable in one assignment.
public struct CharacterSlideContacts : IEquatable<CharacterSlideContacts>
{
    /// <summary>How many nodes one tick can name.</summary>
    public const int Capacity = 4;

    private Slots _nodes;
    private int _count;

    /// <summary>How many nodes the slide stopped at.</summary>
    public readonly int Count => _count;

    /// <summary>The node the slide met at this place in the order.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is not below <see cref="Count"/>.</exception>
    public readonly Guid this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_count, nameof(index));
            return _nodes[index];
        }
    }

    /// <summary>
    /// Adds a node. The empty id, a node that is there already and one past
    /// the room are left out.
    /// </summary>
    public void Add(Guid node)
    {
        if (node == Guid.Empty || _count >= Capacity || Contains(node))
            return;

        _nodes[_count++] = node;
    }

    /// <summary>Whether the slide stopped at the node.</summary>
    public readonly bool Contains(Guid node)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_nodes[i] == node)
                return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public readonly bool Equals(CharacterSlideContacts other)
    {
        if (_count != other._count)
            return false;

        for (int i = 0; i < _count; i++)
        {
            if (_nodes[i] != other._nodes[i])
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is CharacterSlideContacts other && Equals(other);

    /// <inheritdoc/>
    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        for (int i = 0; i < _count; i++)
            hash.Add(_nodes[i]);

        return hash.ToHashCode();
    }

    public static bool operator ==(CharacterSlideContacts left, CharacterSlideContacts right) => left.Equals(right);

    public static bool operator !=(CharacterSlideContacts left, CharacterSlideContacts right) => !left.Equals(right);

    // The runtime's own Equals does not look into an inline array, which is
    // why the struct around it compares by hand.
    [InlineArray(Capacity)]
    private struct Slots
    {
        private Guid _first;
    }
}
