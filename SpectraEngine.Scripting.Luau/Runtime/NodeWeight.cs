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

    // What a node's attributes are kept in, once it has one. Measured at
    // about 125 bytes with the first attribute in it.
    public const int OfAttributes = 128;

    // A name's place beside its value: 48 bytes while a node has few, a
    // dictionary's entry past that.
    public const int OfAttribute = 64;

    // A string's header, beside two bytes for each character.
    public const int OfText = 32;

    // The header of an attribute's text too long to be kept in its value,
    // beside a byte for each of its bytes.
    public const int OfBytes = 24;

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
        foreach (KeyValuePair<string, MemberValue> pair in set)
            bytes += OfAttribute + OfText + (2L * pair.Key.Length) + TextOf(pair.Value);

        return bytes;
    }

    private static long TextOf(in MemberValue value)
    {
        if (value.Kind != MemberValueKind.Text)
            return 0;
        if (!value.IsUtf8)
            return OfText + (2L * value.Text.Length);

        int length = value.Utf8.Length;
        return length > MemberValue.InlineUtf8Bytes ? OfBytes + length : 0;
    }
}
