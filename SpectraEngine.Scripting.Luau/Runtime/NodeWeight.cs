using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// About how many bytes a destroyed node keeps alive while a script holds
// it: the node, its attributes and its tags. An estimate that errs upward,
// since it is what the collector and the memory budget are told.
internal static class NodeWeight
{
    // A part with its brush and its transform, measured at about 1.2 KiB.
    public const int OfNode = 1024;

    // The table a node's attributes are in, once it has one.
    public const int OfAttributes = 384;

    // A dictionary entry with its value.
    public const int OfAttribute = 128;

    // A string's header, beside two bytes for each character.
    public const int OfText = 32;

    // A tag only destroyed nodes have is kept by each of them alone, with
    // its empty lists. Every tag is weighed as one, beside its name.
    public const int OfTag = 256;

    public static long Of(SceneNode node, TagTable tags, AttributeTable attributes)
    {
        long bytes = OfNode;
        IReadOnlyList<TagNodes> own = tags.Of(node);
        for (int i = 0; i < own.Count; i++)
            bytes += OfTag + (2L * own[i].Tag.Length);

        if (attributes.Of(node) is not { } set)
            return bytes;

        // A name can be one no other node has, so each is weighed here.
        bytes += OfAttributes;
        foreach (KeyValuePair<string, MemberValue> pair in set.Values)
        {
            bytes += OfAttribute + OfText + (2L * pair.Key.Length);
            if (pair.Value.Kind == MemberValueKind.Text)
                bytes += OfText + (2L * pair.Value.Text.Length);
        }

        return bytes;
    }
}
