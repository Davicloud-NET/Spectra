using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What AddTag, RemoveTag, HasTag, GetTags and GetTagged do with what a
// script handed them. A node's own methods and those of CollectionService
// share them. The service takes the node as its first argument, so its tag
// is one value further on, and `tagAt` says where.
internal sealed class TagMethods(LevelTags tags)
{
    private const string NotText = "was handed a tag that is not valid UTF-8, which no node can have";

    public int Add(LuauStack stack, SceneNode node, int tagAt)
    {
        if (!stack.TryGetUtf8(tagAt, out ReadOnlySpan<byte> utf8))
            return ScriptArguments.Wrong(stack, tagAt, "AddTag", "string", isMethod: true);

        // The tag is compared where it lies, so one the node has makes no string.
        Span<char> room = stackalloc char[NameChars.Room];
        if (!NameChars.TryRead(utf8, room, out ReadOnlySpan<char> tag))
            return stack.Fail($"AddTag {NotText}");

        tags.Add(node, tag);
        return 0;
    }

    public int Remove(LuauStack stack, SceneNode node, int tagAt)
    {
        if (!stack.TryGetUtf8(tagAt, out ReadOnlySpan<byte> utf8))
            return ScriptArguments.Wrong(stack, tagAt, "RemoveTag", "string", isMethod: true);

        Span<char> room = stackalloc char[NameChars.Room];
        if (NameChars.TryRead(utf8, room, out ReadOnlySpan<char> tag))
            tags.Remove(node, tag);

        return 0;
    }

    public int Has(LuauStack stack, SceneNode node, int tagAt)
    {
        if (!stack.TryGetUtf8(tagAt, out ReadOnlySpan<byte> utf8))
            return ScriptArguments.Wrong(stack, tagAt, "HasTag", "string", isMethod: true);

        Span<char> room = stackalloc char[NameChars.Room];
        bool has = NameChars.TryRead(utf8, room, out ReadOnlySpan<char> tag) && tags.Table.Has(node, tag);
        return ScriptResult.Of(stack.Push(has));
    }

    // GetTags: the node's tags, in the order it got them.
    public int PushTagsOf(LuauStack stack, SceneNode node)
    {
        IReadOnlyList<TagNodes> own = tags.Table.Of(node);
        if (!stack.NewTable(own.Count))
            return LuauStack.Raise;

        int table = stack.Count;
        for (int i = 0; i < own.Count; i++)
        {
            if (!stack.Push(own[i].Tag) || !stack.SetElement(table, i + 1))
                return LuauStack.Raise;
        }

        return 1;
    }

    // GetTagged: the nodes of the level with the tag, in the tree's order.
    public int PushTagged(ScriptContext context, LuauStack stack, int tagAt)
    {
        if (!stack.TryGetUtf8(tagAt, out ReadOnlySpan<byte> utf8))
            return ScriptArguments.Wrong(stack, tagAt, "GetTagged", "string", isMethod: true);

        Span<char> room = stackalloc char[NameChars.Room];
        IReadOnlyList<SceneNode> tagged = NameChars.TryRead(utf8, room, out ReadOnlySpan<char> tag) ? tags.Tagged(tag) : [];
        if (!stack.NewTable(tagged.Count))
            return LuauStack.Raise;

        // Pushing runs no script, so the list holds still.
        int table = stack.Count;
        for (int i = 0; i < tagged.Count; i++)
        {
            if (!context.PushObject(stack, tagged[i]) || !stack.SetElement(table, i + 1))
                return LuauStack.Raise;
        }

        return 1;
    }

    // GetInstanceAddedSignal and GetInstanceRemovedSignal.
    public int PushSignal(ScriptContext context, LuauStack stack, int tagAt, bool isOfAdded)
    {
        string method = isOfAdded ? "GetInstanceAddedSignal" : "GetInstanceRemovedSignal";
        if (!stack.TryGetUtf8(tagAt, out ReadOnlySpan<byte> utf8))
            return ScriptArguments.Wrong(stack, tagAt, method, "string", isMethod: true);

        Span<char> room = stackalloc char[NameChars.Room];
        if (!NameChars.TryRead(utf8, room, out ReadOnlySpan<char> tag))
            return stack.Fail($"{method} {NotText}");

        ScriptSignal signal = isOfAdded ? tags.AddedSignal(tag) : tags.RemovedSignal(tag);
        return ScriptResult.Of(context.PushObject(stack, signal));
    }
}
