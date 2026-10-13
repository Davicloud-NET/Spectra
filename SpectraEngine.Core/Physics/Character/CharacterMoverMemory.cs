using System;
using System.Runtime.CompilerServices;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// A few numbers a mover keeps from one tick to the next, such as the jumps
/// it has made since it left the ground. The built-in mover uses none. All
/// are zero when the character is placed.
/// </summary>
// In the state and with no references, so a rollback copies what a replaced
// mover remembers with the rest. Kept anywhere else, a replay would not match.
public struct CharacterMoverMemory : IEquatable<CharacterMoverMemory>
{
    /// <summary>How many numbers a mover can keep.</summary>
    public const int Capacity = 8;

    private Slots _numbers;

    /// <summary>The number in this place.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is not below <see cref="Capacity"/>.</exception>
    public float this[int index]
    {
        readonly get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Capacity, nameof(index));
            return _numbers[index];
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Capacity, nameof(index));
            _numbers[index] = value;
        }
    }

    /// <inheritdoc/>
    // By bits: a replay has to give the same state, and -0 is not 0 there.
    public readonly bool Equals(CharacterMoverMemory other)
    {
        for (int i = 0; i < Capacity; i++)
        {
            if (BitConverter.SingleToInt32Bits(_numbers[i]) != BitConverter.SingleToInt32Bits(other._numbers[i]))
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is CharacterMoverMemory other && Equals(other);

    /// <inheritdoc/>
    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        for (int i = 0; i < Capacity; i++)
            hash.Add(BitConverter.SingleToInt32Bits(_numbers[i]));

        return hash.ToHashCode();
    }

    public static bool operator ==(CharacterMoverMemory left, CharacterMoverMemory right) => left.Equals(right);

    public static bool operator !=(CharacterMoverMemory left, CharacterMoverMemory right) => !left.Equals(right);

    // The runtime's own Equals does not look into an inline array.
    [InlineArray(Capacity)]
    private struct Slots
    {
        private float _first;
    }
}
