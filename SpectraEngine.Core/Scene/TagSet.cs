using SpectraEngine.Core.Scripting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// The tags a node was built with, in the order it got them. It never
/// changes once made: an edit makes a new one. Every tag keeps
/// <see cref="TagName"/>'s rule, none is there twice, and there are at most
/// <see cref="TagLimits.MaxPerNode"/>. A tag is case sensitive.
/// </summary>
public sealed class TagSet : IReadOnlyList<string>, IEquatable<TagSet>
{
    private readonly string[] _tags;

    private TagSet(string[] tags) => _tags = tags;

    /// <summary>No tags.</summary>
    public static TagSet Empty { get; } = new([]);

    /// <inheritdoc/>
    public int Count => _tags.Length;

    /// <inheritdoc/>
    public string this[int index] => _tags[index];

    /// <summary>The set of these tags, in this order.</summary>
    /// <exception cref="ArgumentException">A tag breaks the rule or is there twice, or there are too many.</exception>
    public static TagSet Of(params IReadOnlyList<string> tags) =>
        TryMake(tags, out TagSet? made, out string? refusal) ? made : throw new ArgumentException(refusal, nameof(tags));

    /// <summary>The set of these tags, in this order, or why they are no set.</summary>
    public static bool TryMake(
        IReadOnlyList<string> tags, [NotNullWhen(true)] out TagSet? made, [NotNullWhen(false)] out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(tags);

        made = null;
        refusal = null;
        if (tags.Count > TagLimits.MaxPerNode)
            refusal = $"A node has at most {TagLimits.MaxPerNode} tags, and these are {tags.Count}.";

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < tags.Count && refusal is null; i++)
        {
            if (tags[i] is null || !TagName.IsValid(tags[i]))
                refusal = TagName.Refusal(tags[i] ?? string.Empty) + ".";
            else if (!seen.Add(tags[i]))
                refusal = $"The tag '{tags[i]}' is there twice.";
        }

        if (refusal is not null)
            return false;

        made = tags.Count == 0 ? Empty : new TagSet([.. tags]);
        return true;
    }

    /// <summary>Whether the tag is in the set.</summary>
    public bool Contains(string tag) => Array.IndexOf(_tags, tag) >= 0;

    /// <summary>Whether one more tag would be one too many.</summary>
    public bool IsFull => _tags.Length >= TagLimits.MaxPerNode;

    /// <summary>The set with the tag at its end. This set when it has the tag already.</summary>
    /// <exception cref="ArgumentException">The tag breaks the rule, or the set is full.</exception>
    public TagSet With(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (Contains(tag))
            return this;
        if (!TagName.IsValid(tag))
            throw new ArgumentException(TagName.Refusal(tag), nameof(tag));
        if (IsFull)
            throw new ArgumentException($"A node has at most {TagLimits.MaxPerNode} tags.", nameof(tag));

        return new TagSet([.. _tags, tag]);
    }

    /// <summary>The set with the tag taken out. This set when it lacks the tag.</summary>
    public TagSet Without(string tag)
    {
        int index = Array.IndexOf(_tags, tag);
        if (index < 0)
            return this;
        if (_tags.Length == 1)
            return Empty;

        var rest = new string[_tags.Length - 1];
        Array.Copy(_tags, 0, rest, 0, index);
        Array.Copy(_tags, index + 1, rest, index, rest.Length - index);
        return new TagSet(rest);
    }

    /// <summary>Whether both hold the same tags in the same order.</summary>
    public bool Equals(TagSet? other) =>
        other is not null && (ReferenceEquals(this, other) || _tags.AsSpan().SequenceEqual(other._tags));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TagSet);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (string tag in _tags)
            hash.Add(tag, StringComparer.Ordinal);

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_tags).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _tags.GetEnumerator();
}
