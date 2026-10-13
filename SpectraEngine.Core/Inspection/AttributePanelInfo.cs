using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Inspection;

/// <summary>
/// What a panel needs to show the attributes of the selection: every
/// attribute a selected node was built with, in the order they are first
/// met, merged over the nodes. Holds copies, never a node.
/// </summary>
public sealed class AttributePanelInfo
{
    private static readonly IReadOnlySet<string> NoNames = new HashSet<string>();

    // The sets this was made from, node for node.
    private readonly AttributeSet[] _sources;

    private AttributePanelInfo(
        AttributeSet[] sources, AttributePanelRow[] rows, bool hasFullNode, IReadOnlySet<string> entityNames)
    {
        _sources = sources;
        Rows = rows;
        HasFullNode = hasFullNode;
        EntityNames = entityNames;
    }

    /// <summary>How many nodes are selected.</summary>
    public int SelectionCount => _sources.Length;

    /// <summary>The attributes, each name once.</summary>
    public IReadOnlyList<AttributePanelRow> Rows { get; }

    /// <summary>Whether a selected node holds as many attributes as a node can.</summary>
    public bool HasFullNode { get; }

    /// <summary>
    /// The names an entity on a selected node answers to: its keyvalues. A
    /// new attribute cannot have one of them.
    /// </summary>
    public IReadOnlySet<string> EntityNames { get; }

    /// <summary>
    /// Describes the attributes of <paramref name="nodes"/>, or null for
    /// none. Render thread only.
    /// </summary>
    /// <param name="schemas">What the entity classes declare, or null.</param>
    /// <param name="previous">
    /// What the last call gave. Handed back when nothing changed, so a reader
    /// can tell by the reference.
    /// </param>
    public static AttributePanelInfo? Capture(
        IReadOnlyList<SceneNode> nodes, EntitySchemaCatalog? schemas = null, AttributePanelInfo? previous = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (nodes.Count == 0)
            return null;

        if (previous is not null && IsMadeFrom(previous, nodes))
            return previous;

        var sources = new AttributeSet[nodes.Count];
        var rows = new List<AttributePanelRow>();
        var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
        HashSet<string>? entityNames = null;
        bool hasFullNode = false;

        for (int i = 0; i < nodes.Count; i++)
        {
            SceneNode node = nodes[i];
            AttributeSet set = sources[i] = node.Attributes;
            hasFullNode |= set.IsFull;
            if (node.Entity is not null)
                EntityKeyvalueNames.AddTo(node, schemas, entityNames ??= new HashSet<string>(StringComparer.Ordinal));

            foreach (KeyValuePair<string, MemberValue> pair in set)
            {
                bool isTaken = node.Entity is not null && EntityKeyvalueNames.Contains(node, schemas, pair.Key);
                if (indexOf.TryGetValue(pair.Key, out int at))
                {
                    rows[at] = Merged(rows[at], pair.Value, isTaken);
                    continue;
                }

                indexOf.Add(pair.Key, rows.Count);
                rows.Add(new AttributePanelRow(pair.Key, pair.Value.Kind, pair.Value, 1, 0, isTaken));
            }
        }

        return new AttributePanelInfo(sources, [.. rows], hasFullNode, entityNames ?? NoNames);
    }

    private static AttributePanelRow Merged(AttributePanelRow row, in MemberValue value, bool isTaken)
    {
        MemberValue first = row.Value;
        bool sameKind = row.Kind == value.Kind;

        return row with
        {
            Kind = sameKind ? row.Kind : MemberValueKind.None,
            Count = row.Count + 1,
            MixedParts = sameKind ? row.MixedParts | Difference(in first, in value) : ~0,
            IsTakenByEntity = row.IsTakenByEntity || isTaken,
        };
    }

    // Exact comparison, no tolerance: values reported equal get written
    // over each other by the next edit of the whole selection.
    private static int Difference(in MemberValue a, in MemberValue b)
    {
        int parts = AttributeParts.CountOf(a.Kind);
        if (parts == 0)
            return a == b ? 0 : 1;

        // A rotation has no part of its own: when two differ, all three do.
        int mixed = a.Kind == MemberValueKind.CFrame && a.Frame.Rotation != b.Frame.Rotation ? 0b111000 : 0;
        for (int part = 0; part < Math.Min(parts, 3); part++)
        {
            if (AttributeParts.Get(in a, part) != AttributeParts.Get(in b, part))
                mixed |= 1 << part;
        }

        return mixed;
    }

    // A set never changes, so the same references are the same attributes.
    // What an entity answers to can change under one, so a selection with
    // an entity in it is described anew.
    private static bool IsMadeFrom(AttributePanelInfo info, IReadOnlyList<SceneNode> nodes)
    {
        if (info._sources.Length != nodes.Count || info.EntityNames.Count > 0)
            return false;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (!ReferenceEquals(info._sources[i], nodes[i].Attributes) || nodes[i].Entity is not null)
                return false;
        }

        return true;
    }
}
