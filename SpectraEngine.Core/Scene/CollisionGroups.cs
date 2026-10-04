using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// The named collision groups of one world, and the 64×64 matrix saying which
/// pairs of them interact. Group 0 is <c>Default</c>, and every pair
/// interacts until one is disabled.
/// </summary>
public sealed class CollisionGroups
{
    /// <summary>The maximum number of groups one world may name.</summary>
    public const int MaxGroups = 64;

    /// <summary>The id every node starts in.</summary>
    public const int DefaultGroup = 0;

    /// <summary>The reserved name of <see cref="DefaultGroup"/>.</summary>
    public const string DefaultGroupName = "Default";

    private readonly List<string> _names = [DefaultGroupName];
    private readonly Dictionary<string, int> _ids =
        new(StringComparer.Ordinal) { [DefaultGroupName] = DefaultGroup };

    // Bit j of _masks[i]: group i collides with group j. Every write must
    // touch both halves to keep it symmetric.
    private readonly ulong[] _masks = new ulong[MaxGroups];

    /// <summary>Creates a registry holding only <see cref="DefaultGroupName"/>, with everything colliding.</summary>
    public CollisionGroups()
    {
        for (int i = 0; i < MaxGroups; i++)
            _masks[i] = ulong.MaxValue;
    }

    /// <summary>How many groups are currently named, <see cref="DefaultGroupName"/> included.</summary>
    public int Count => _names.Count;

    /// <summary>The names in id order.</summary>
    public IReadOnlyList<string> Names => _names;

    /// <summary>
    /// Returns the id of <paramref name="name"/>, registering it if new.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// All <see cref="MaxGroups"/> ids are already taken.
    /// </exception>
    public int Register(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (_ids.TryGetValue(name, out int existing))
            return existing;

        if (_names.Count >= MaxGroups)
        {
            throw new InvalidOperationException(
                $"Cannot register collision group '{name}': all {MaxGroups} groups are taken " +
                $"({string.Join(", ", _names)}). Collision groups are a fixed 64-bit filter — " +
                "reuse an existing group or remove one that is no longer needed.");
        }

        int id = _names.Count;
        _names.Add(name);
        _ids[name] = id;
        return id;
    }

    /// <summary>Resolves a group name to its id, or false when it was never registered.</summary>
    public bool TryGetId(string name, out int id) => _ids.TryGetValue(name, out id);

    /// <summary>The name of <paramref name="id"/>.</summary>
    public string GetName(int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, _names.Count);
        return _names[id];
    }

    /// <summary>Sets whether two groups interact. Symmetric.</summary>
    public void SetCollidable(int groupA, int groupB, bool collidable)
    {
        ValidateId(groupA, nameof(groupA));
        ValidateId(groupB, nameof(groupB));

        if (collidable)
        {
            _masks[groupA] |= 1UL << groupB;
            _masks[groupB] |= 1UL << groupA;
        }
        else
        {
            _masks[groupA] &= ~(1UL << groupB);
            _masks[groupB] &= ~(1UL << groupA);
        }
    }

    /// <summary>
    /// Whether two groups interact. Throws on an id that names no registered
    /// group; a query traversal should use <see cref="Interacts"/>.
    /// </summary>
    public bool AreCollidable(int groupA, int groupB)
    {
        ValidateId(groupA, nameof(groupA));
        ValidateId(groupB, nameof(groupB));
        return (_masks[groupA] & (1UL << groupB)) != 0;
    }

    /// <summary>
    /// Whether two ids interact, without throwing. An id in range but not yet
    /// named interacts with everything.
    /// </summary>
    // A node can carry an id this registry has not named (assigned before
    // attach, or restored before the names), and a broad-phase walk must not
    // throw halfway through.
    public bool Interacts(int groupA, int groupB) =>
        (uint)groupA < MaxGroups && (uint)groupB < MaxGroups &&
        (_masks[groupA] & (1UL << groupB)) != 0;

    /// <summary>
    /// The raw mask word for <paramref name="group"/>: bit <c>j</c> set means
    /// it interacts with group <c>j</c>.
    /// </summary>
    public ulong GetMask(int group)
    {
        ValidateId(group, nameof(group));
        return _masks[group];
    }

    private void ValidateId(int id, string paramName)
    {
        if (id < 0 || id >= _names.Count)
        {
            throw new ArgumentOutOfRangeException(
                paramName, id,
                $"Collision group id must name a registered group (0..{_names.Count - 1}).");
        }
    }
}
