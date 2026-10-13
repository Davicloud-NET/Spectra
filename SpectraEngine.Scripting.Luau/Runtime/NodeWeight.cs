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

    // A dictionary entry with its value.
    public const int OfAttribute = 128;

    // A string's header, beside two bytes for each character.
    public const int OfText = 32;

    public const int OfTag = 8;

    public static long Of(SceneNode node, TagTable tags, AttributeTable attributes)
    {
        long bytes = OfNode + (long)tags.Of(node).Count * OfTag;
        if (attributes.Of(node) is not { } own)
            return bytes;

        foreach (KeyValuePair<string, MemberValue> pair in own.Values)
        {
            bytes += OfAttribute;
            if (pair.Value.Kind == MemberValueKind.Text)
                bytes += OfText + (2L * pair.Value.Text.Length);
        }

        return bytes;
    }
}
