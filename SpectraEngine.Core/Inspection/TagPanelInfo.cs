using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// What a panel needs to show the tags of the selection: every tag a selected
/// node was built with, in the order they are first met, and how many of the
/// nodes have each. Holds copies, never a node.
/// </summary>
public sealed class TagPanelInfo
{
    // The sets this was made from, node for node. The same sets make the same info.
    private readonly TagSet[] _sources;

    private TagPanelInfo(TagSet[] sources, TagPanelRow[] tags, bool hasFullNode)
    {
        _sources = sources;
        Tags = tags;
        HasFullNode = hasFullNode;
    }

    /// <summary>How many nodes are selected.</summary>
    public int SelectionCount => _sources.Length;

    /// <summary>The tags, each once.</summary>
    public IReadOnlyList<TagPanelRow> Tags { get; }

    /// <summary>Whether a selected node has as many tags as a node can have.</summary>
    public bool HasFullNode { get; }

    /// <summary>
    /// Describes the tags of <paramref name="nodes"/>, or null for none.
    /// Render thread only.
    /// </summary>
    /// <param name="previous">
    /// What the last call gave. Handed back when nothing changed, so a reader
    /// can tell by the reference.
    /// </param>
    public static TagPanelInfo? Capture(IReadOnlyList<SceneNode> nodes, TagPanelInfo? previous = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (nodes.Count == 0)
            return null;

        if (previous is not null && IsMadeFrom(previous, nodes))
            return previous;

        var sources = new TagSet[nodes.Count];
        var rows = new List<TagPanelRow>();
        var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
        bool hasFullNode = false;

        for (int i = 0; i < nodes.Count; i++)
        {
            TagSet tags = sources[i] = nodes[i].Tags;
            hasFullNode |= tags.IsFull;

            foreach (string tag in tags)
            {
                if (indexOf.TryGetValue(tag, out int at))
                {
                    rows[at] = rows[at] with { Count = rows[at].Count + 1 };
                    continue;
                }

                indexOf.Add(tag, rows.Count);
                rows.Add(new TagPanelRow(tag, 1));
            }
        }

        return new TagPanelInfo(sources, [.. rows], hasFullNode);
    }

    // A set never changes, so the same references are the same tags.
    private static bool IsMadeFrom(TagPanelInfo info, IReadOnlyList<SceneNode> nodes)
    {
        if (info._sources.Length != nodes.Count)
            return false;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (!ReferenceEquals(info._sources[i], nodes[i].Tags))
                return false;
        }

        return true;
    }
}
