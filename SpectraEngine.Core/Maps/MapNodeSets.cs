using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Maps;

// A document node's tags and attributes as the sets a scene node holds. The
// reader has checked what it read. A document built in code has not been
// through it, so what breaks a rule is refused here by the node's name.
internal static class MapNodeSets
{
    public static TagSet TagsOf(MapNode mapped) =>
        TagSet.TryMake(mapped.Tags, out TagSet? tags, out string? refusal)
            ? tags
            : throw new MapFormatException($"These tags cannot be kept: {refusal}", mapped.Name, mapped.SourceOffset);

    public static AttributeSet AttributesOf(MapNode mapped) =>
        AttributeSet.TryMake(mapped.Attributes, out AttributeSet? attributes, out string? refusal)
            ? attributes
            : throw new MapFormatException(
                $"These attributes cannot be kept: {refusal}", mapped.Name, mapped.SourceOffset);
}
