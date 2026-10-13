using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Scripting;

// Copies a node with everything below it for TreeWrites, and turns what the
// copy says of the copied tree towards itself.
internal static class NodeCopies
{
    // Fills the list with each node beside its copy, the top first, and
    // builds the copy's tree. False when there are more nodes than `most`:
    // the list then holds what was copied so far, which is in no tree.
    // The list is its own queue, so no walk goes deeper than one call.
    public static bool Fill(SceneNode source, List<NodeCopy> copies, int most)
    {
        if (most < 1)
            return false;

        copies.Add(new NodeCopy(source, source.Clone(deep: false)));
        for (int i = 0; i < copies.Count; i++)
        {
            (SceneNode from, SceneNode to) = copies[i];
            IReadOnlyList<SceneNode> children = from.Children;
            if (copies.Count + children.Count > most)
                return false;

            for (int c = 0; c < children.Count; c++)
            {
                SceneNode child = children[c].Clone(deep: false);
                to.AddChild(child);
                copies.Add(new NodeCopy(children[c], child));
            }
        }

        return true;
    }

    // A keyvalue or a wire's parameter that names a copied node by its id
    // names the copy from here on. One that names a node outside is kept.
    public static void PointIntoTheCopy(List<NodeCopy> copies)
    {
        Dictionary<Guid, Guid>? ids = null;
        for (int i = 0; i < copies.Count; i++)
        {
            if (copies[i].Copy.Entity is not { } entity || !HoldsText(entity))
                continue;

            ids ??= IdsOf(copies);
            Point(entity, ids);
        }
    }

    private static bool HoldsText(EntityData entity) => entity.Keyvalues.Count > 0 || entity.Connections.Count > 0;

    private static Dictionary<Guid, Guid> IdsOf(List<NodeCopy> copies)
    {
        var ids = new Dictionary<Guid, Guid>(copies.Count);
        for (int i = 0; i < copies.Count; i++)
            ids[copies[i].Source.Id] = copies[i].Copy.Id;

        return ids;
    }

    private static void Point(EntityData entity, Dictionary<Guid, Guid> ids)
    {
        List<KeyValuePair<string, string>> keyvalues = entity.Keyvalues;
        for (int i = 0; i < keyvalues.Count; i++)
        {
            if (Copied(keyvalues[i].Value, ids) is { } copy)
                keyvalues[i] = new KeyValuePair<string, string>(keyvalues[i].Key, copy);
        }

        List<EntityConnection> wires = entity.Connections;
        for (int i = 0; i < wires.Count; i++)
        {
            if (Copied(wires[i].Parameter, ids) is { } copy)
                wires[i] = wires[i] with { Parameter = copy };
        }
    }

    // The id of the copy, as a node reference is written. Null for text
    // that names no copied node.
    private static string? Copied(string text, Dictionary<Guid, Guid> ids) =>
        KeyvalueWire.TryParseNodeRef(text, out Guid id) && ids.TryGetValue(id, out Guid copy)
            ? copy.ToString("D")
            : null;
}
