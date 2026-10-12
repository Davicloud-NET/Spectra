using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The members that walk the tree: what is below a node, what is above it,
// and what it is. A name comes as the bytes Luau keeps, so a search makes
// no string.
internal static partial class NodeApi
{
    /// <summary>The first child with that name, or nil. With recursive, the first node anywhere below: a node's children are looked at before what is below them.</summary>
    [ScriptMethod]
    public static SceneNode? FindFirstChild(SceneNode node, ReadOnlySpan<byte> name, bool recursive = false) =>
        Below(node, NodeMatch.Name, name, recursive);

    /// <summary>The first child whose ClassName is that class, or nil. A class that extends it does not count.</summary>
    [ScriptMethod]
    public static SceneNode? FindFirstChildOfClass(SceneNode node, ReadOnlySpan<byte> className) =>
        Below(node, NodeMatch.ClassName, className, recursive: false);

    /// <summary>The first child that is of that class or of one that extends it, or nil. With recursive, the first node anywhere below.</summary>
    [ScriptMethod]
    public static SceneNode? FindFirstChildWhichIsA(SceneNode node, ReadOnlySpan<byte> className, bool recursive = false) =>
        Below(node, NodeMatch.IsA, className, recursive);

    /// <summary>The nearest node above this one with that name, or nil.</summary>
    [ScriptMethod]
    public static SceneNode? FindFirstAncestor(SceneNode node, ReadOnlySpan<byte> name) =>
        Above(node, NodeMatch.Name, name);

    /// <summary>The nearest node above this one whose ClassName is that class, or nil.</summary>
    [ScriptMethod]
    public static SceneNode? FindFirstAncestorOfClass(SceneNode node, ReadOnlySpan<byte> className) =>
        Above(node, NodeMatch.ClassName, className);

    /// <summary>The nearest node above this one that is of that class or of one that extends it, or nil.</summary>
    [ScriptMethod]
    public static SceneNode? FindFirstAncestorWhichIsA(SceneNode node, ReadOnlySpan<byte> className) =>
        Above(node, NodeMatch.IsA, className);

    /// <summary>The child with that name. When there is none, the script waits here until one arrives. With a timeout in seconds it then stops waiting and gets nil.</summary>
    [ScriptMethod]
    [ScriptOverload("name: string", "Instance")]
    [ScriptOverload("name: string, timeout: number", "Instance?")]
    private static int WaitForChild(ScriptContext context, LuauStack stack, SceneNode node) =>
        context.ChildWaits.Wait(context, stack, node);

    /// <summary>The node's children, in the order of the tree.</summary>
    [ScriptMethod(Returns = "{Instance}")]
    private static int GetChildren(ScriptContext context, LuauStack stack, SceneNode node)
    {
        IReadOnlyList<SceneNode> children = node.Children;
        if (!stack.NewTable(children.Count))
            return LuauStack.Raise;

        int table = stack.Count;
        for (int i = 0; i < children.Count; i++)
        {
            if (!context.PushObject(stack, children[i]) || !stack.SetElement(table, i + 1))
                return LuauStack.Raise;
        }

        return 1;
    }

    /// <summary>Every node below this one, each before the nodes below it.</summary>
    [ScriptMethod(Returns = "{Instance}")]
    private static int GetDescendants(ScriptContext context, LuauStack stack, SceneNode node)
    {
        if (!stack.NewTable(NodeTree.CountBelow(node)))
            return LuauStack.Raise;

        int count = 0;
        return PushBelow(context, stack, node, stack.Count, ref count) ? 1 : LuauStack.Raise;
    }

    /// <summary>Whether the other node is anywhere below this one.</summary>
    [ScriptMethod]
    public static bool IsAncestorOf(SceneNode node, SceneNode descendant) => NodeTree.IsAbove(node, descendant);

    /// <summary>Whether this node is anywhere below the other one.</summary>
    [ScriptMethod]
    public static bool IsDescendantOf(SceneNode node, SceneNode ancestor) => NodeTree.IsAbove(ancestor, node);

    /// <summary>The names from the root of the level down to this node, with a dot between.</summary>
    [ScriptMethod]
    public static string GetFullName(SceneNode node) => NodeTree.FullName(node);

    /// <summary>Whether the node is of that class or of one that extends it. A door is its entity class, a Part, a BasePart and an Instance.</summary>
    [ScriptMethod]
    public static bool IsA(SceneNode node, ReadOnlySpan<byte> className)
    {
        Span<char> room = stackalloc char[NameChars.Room];
        return NameChars.TryRead(className, room, out ReadOnlySpan<char> wanted) && NodeClass.IsA(node, wanted);
    }

    private static SceneNode? Below(SceneNode node, NodeMatch by, ReadOnlySpan<byte> text, bool recursive)
    {
        Span<char> room = stackalloc char[NameChars.Room];
        if (!NameChars.TryRead(text, room, out ReadOnlySpan<char> wanted))
            return null;

        return recursive ? NodeTree.Descendant(node, by, wanted) : NodeTree.Child(node, by, wanted);
    }

    private static SceneNode? Above(SceneNode node, NodeMatch by, ReadOnlySpan<byte> text)
    {
        Span<char> room = stackalloc char[NameChars.Room];
        return NameChars.TryRead(text, room, out ReadOnlySpan<char> wanted) ? NodeTree.Ancestor(node, by, wanted) : null;
    }

    // In the order of GetDescendants. False: an error value is on top.
    private static bool PushBelow(ScriptContext context, LuauStack stack, SceneNode node, int table, ref int count)
    {
        IReadOnlyList<SceneNode> children = node.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (!context.PushObject(stack, children[i]) || !stack.SetElement(table, ++count))
                return false;
            if (!PushBelow(context, stack, children[i], table, ref count))
                return false;
        }

        return true;
    }
}
