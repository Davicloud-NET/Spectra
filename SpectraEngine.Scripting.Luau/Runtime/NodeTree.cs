using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The searches of the level's tree a script asks for. Each walks the nodes
// in place and makes nothing.
internal static class NodeTree
{
    // The first child that matches, in the tree's order.
    public static SceneNode? Child(SceneNode parent, NodeMatch by, ReadOnlySpan<char> text)
    {
        IReadOnlyList<SceneNode> children = parent.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (Matches(children[i], by, text))
                return children[i];
        }

        return null;
    }

    // The first node below that matches. A node's own children are looked at
    // before what is below any of them, which is the order Roblox searches in.
    public static SceneNode? Descendant(SceneNode parent, NodeMatch by, ReadOnlySpan<char> text)
    {
        if (Child(parent, by, text) is { } child)
            return child;

        IReadOnlyList<SceneNode> children = parent.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (Descendant(children[i], by, text) is { } below)
                return below;
        }

        return null;
    }

    // The nearest node above that matches.
    public static SceneNode? Ancestor(SceneNode node, NodeMatch by, ReadOnlySpan<char> text)
    {
        for (SceneNode? above = node.Parent; above is not null; above = above.Parent)
        {
            if (Matches(above, by, text))
                return above;
        }

        return null;
    }

    // A node is neither above nor below itself.
    public static bool IsAbove(SceneNode node, SceneNode other)
    {
        for (SceneNode? above = other.Parent; above is not null; above = above.Parent)
        {
            if (ReferenceEquals(above, node))
                return true;
        }

        return false;
    }

    public static int CountBelow(SceneNode node)
    {
        IReadOnlyList<SceneNode> children = node.Children;
        int count = children.Count;
        for (int i = 0; i < children.Count; i++)
            count += CountBelow(children[i]);

        return count;
    }

    // The names from the root down to the node, with a dot between.
    public static string FullName(SceneNode node)
    {
        int length = -1;
        for (SceneNode? at = node; at is not null; at = at.Parent)
            length += at.Name.Length + 1;

        return string.Create(length, node, static (text, leaf) =>
        {
            int end = text.Length;
            for (SceneNode? at = leaf; at is not null; at = at.Parent)
            {
                end -= at.Name.Length;
                at.Name.CopyTo(text[end..]);
                if (end > 0)
                    text[--end] = '.';
            }
        });
    }

    private static bool Matches(SceneNode node, NodeMatch by, ReadOnlySpan<char> text) => by switch
    {
        NodeMatch.Name => text.SequenceEqual(node.Name),
        NodeMatch.ClassName => text.SequenceEqual(NodeClassName.Of(node)),
        _ => NodeClass.IsA(node, text),
    };
}
