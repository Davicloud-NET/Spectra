using SpectraEngine.Core.Scripting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// The attributes a node was built with: a value under each name, in the
/// order they were added. It never changes once made: an edit makes a new
/// one. Every name keeps <see cref="AttributeName"/>'s rule and every value
/// <see cref="AttributeValueRule"/>'s, no name is there twice, and there are
/// at most <see cref="AttributeLimits.MaxPerNode"/>. Text is kept as UTF-8.
/// </summary>
public sealed class AttributeSet : IReadOnlyList<KeyValuePair<string, MemberValue>>, IEquatable<AttributeSet>
{
    private readonly KeyValuePair<string, MemberValue>[] _pairs;

    private AttributeSet(KeyValuePair<string, MemberValue>[] pairs) => _pairs = pairs;

    /// <summary>No attributes.</summary>
    public static AttributeSet Empty { get; } = new([]);

    /// <inheritdoc/>
    public int Count => _pairs.Length;

    /// <inheritdoc/>
    public KeyValuePair<string, MemberValue> this[int index] => _pairs[index];

    /// <summary>Whether one more name would be one too many.</summary>
    public bool IsFull => _pairs.Length >= AttributeLimits.MaxPerNode;

    /// <summary>The set of these attributes, in this order, or why they are no set.</summary>
    public static bool TryMake(
        IReadOnlyList<KeyValuePair<string, MemberValue>> pairs,
        [NotNullWhen(true)] out AttributeSet? made,
        [NotNullWhen(false)] out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        made = null;
        refusal = null;
        if (pairs.Count > AttributeLimits.MaxPerNode)
            refusal = $"A node holds at most {AttributeLimits.MaxPerNode} attributes, and these are {pairs.Count}.";

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < pairs.Count && refusal is null; i++)
        {
            string name = pairs[i].Key ?? string.Empty;
            MemberValue value = pairs[i].Value;

            if (!AttributeName.IsValid(name))
                refusal = AttributeName.Refusal(name) + ".";
            else if (!seen.Add(name))
                refusal = $"The attribute '{name}' is there twice.";
            else if (AttributeValueRule.Refusal(in value) is { } whyNot)
                refusal = $"The attribute '{name}' cannot hold its value. {whyNot}";
        }

        if (refusal is not null)
            return false;

        if (pairs.Count == 0)
        {
            made = Empty;
            return true;
        }

        var kept = new KeyValuePair<string, MemberValue>[pairs.Count];
        for (int i = 0; i < kept.Length; i++)
            kept[i] = new KeyValuePair<string, MemberValue>(pairs[i].Key, pairs[i].Value.AsUtf8());

        made = new AttributeSet(kept);
        return true;
    }

    /// <summary>The value under a name. Names are case sensitive.</summary>
    public bool TryGet(string name, out MemberValue value)
    {
        int index = IndexOf(name);
        value = index >= 0 ? _pairs[index].Value : MemberValue.None;
        return index >= 0;
    }

    /// <summary>Whether the set has an attribute of that name.</summary>
    public bool Contains(string name) => IndexOf(name) >= 0;

    /// <summary>
    /// The set with the value under the name: in the name's place when it is
    /// there, at the end when it is new. This set when it holds that already.
    /// </summary>
    /// <exception cref="ArgumentException">The name or the value breaks its rule, or the set is full.</exception>
    public AttributeSet With(string name, in MemberValue value)
    {
        ArgumentNullException.ThrowIfNull(name);

        int index = IndexOf(name);
        if (index >= 0 && _pairs[index].Value == value)
            return this;
        if (!AttributeName.IsValid(name))
            throw new ArgumentException(AttributeName.Refusal(name), nameof(name));
        if (AttributeValueRule.Refusal(in value) is { } whyNot)
            throw new ArgumentException(whyNot, nameof(value));
        if (index < 0 && IsFull)
            throw new ArgumentException($"A node holds at most {AttributeLimits.MaxPerNode} attributes.", nameof(name));

        KeyValuePair<string, MemberValue>[] next = index >= 0 ? [.. _pairs] : [.. _pairs, default];
        next[index >= 0 ? index : ^1] = new KeyValuePair<string, MemberValue>(name, value.AsUtf8());
        return new AttributeSet(next);
    }

    /// <summary>The set with the name taken out. This set when it lacks the name.</summary>
    public AttributeSet Without(string name)
    {
        int index = IndexOf(name);
        if (index < 0)
            return this;
        if (_pairs.Length == 1)
            return Empty;

        var rest = new KeyValuePair<string, MemberValue>[_pairs.Length - 1];
        Array.Copy(_pairs, 0, rest, 0, index);
        Array.Copy(_pairs, index + 1, rest, index, rest.Length - index);
        return new AttributeSet(rest);
    }

    /// <summary>
    /// The set with one attribute under another name, in the same place. This
    /// set when it lacks the old name or the two names are one.
    /// </summary>
    /// <exception cref="ArgumentException">The new name breaks the rule, or the set has it already.</exception>
    public AttributeSet Renamed(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(to);

        int index = IndexOf(from);
        if (index < 0 || string.Equals(from, to, StringComparison.Ordinal))
            return this;
        if (!AttributeName.IsValid(to))
            throw new ArgumentException(AttributeName.Refusal(to), nameof(to));
        if (Contains(to))
            throw new ArgumentException($"There is an attribute called '{to}' already.", nameof(to));

        KeyValuePair<string, MemberValue>[] next = [.. _pairs];
        next[index] = new KeyValuePair<string, MemberValue>(to, _pairs[index].Value);
        return new AttributeSet(next);
    }

    /// <summary>Whether both hold equal values under the same names in the same order.</summary>
    public bool Equals(AttributeSet? other)
    {
        if (other is null || other._pairs.Length != _pairs.Length)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        for (int i = 0; i < _pairs.Length; i++)
        {
            if (!string.Equals(_pairs[i].Key, other._pairs[i].Key, StringComparison.Ordinal)
                || _pairs[i].Value != other._pairs[i].Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as AttributeSet);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (KeyValuePair<string, MemberValue> pair in _pairs)
            hash.Add(pair.Key, StringComparer.Ordinal);

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, MemberValue>> GetEnumerator() =>
        ((IEnumerable<KeyValuePair<string, MemberValue>>)_pairs).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _pairs.GetEnumerator();

    private int IndexOf(string name)
    {
        for (int i = 0; i < _pairs.Length; i++)
        {
            if (string.Equals(_pairs[i].Key, name, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
