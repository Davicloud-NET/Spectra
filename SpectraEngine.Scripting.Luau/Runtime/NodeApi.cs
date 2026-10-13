using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>A node of the level's tree: a part, a light, a folder, a script.</summary>
// A property with Sets has no body here. The generator writes it, and it
// reads the member of ScriptWrites that a script's write goes through.
// A name that is no member is a child's, which ObjectMembers answers.
[ScriptClass("Instance", ChildType = "Instance")]
internal static partial class NodeApi
{
    /// <summary>The node's name. A wire finds its entity by the same name.</summary>
    [ScriptProperty(Sets = SettableMemberId.Name)]
    public static partial string Name(SceneNode node);

    /// <summary>The node's class: its entity class when it has one, otherwise its kind, such as Part or PointLight.</summary>
    [ScriptProperty]
    public static string ClassName(SceneNode node) => NodeClassName.Of(node);

    /// <summary>The node above this one in the tree. Nil for the root.</summary>
    [ScriptProperty(CannotSet = "Parent cannot be set from a script yet")]
    public static SceneNode? Parent(SceneNode node) => node.Parent;

    /// <summary>Where the node is in the world.</summary>
    [ScriptProperty(Sets = SettableMemberId.Position)]
    public static partial Vector3 Position(SceneNode node);

    /// <summary>Where the node is in the world and which way it is turned.</summary>
    [ScriptProperty(Sets = SettableMemberId.CFrame)]
    public static partial CFrame CFrame(SceneNode node);

    /// <summary>Where the node is under its parent.</summary>
    [ScriptProperty(Sets = SettableMemberId.LocalPosition)]
    public static partial Vector3 LocalPosition(SceneNode node);

    /// <summary>Where the node is under its parent and which way it is turned there.</summary>
    [ScriptProperty(Sets = SettableMemberId.LocalCFrame)]
    public static partial CFrame LocalCFrame(SceneNode node);

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

    /// <summary>Whether the other node is anywhere below this one. Game and nil are below no node.</summary>
    [ScriptMethod(Parameters = "descendant: (Instance | DataModel)?", Returns = "boolean")]
    private static int IsAncestorOf(ScriptContext context, LuauStack stack, SceneNode node)
    {
        if (stack.IsNil(2) || context.TryGetObject(stack, 2, out DataModel? _))
            return ScriptResult.Of(stack.Push(false));

        return context.TryGetObject(stack, 2, out SceneNode? descendant)
            ? ScriptResult.Of(stack.Push(NodeTree.IsAbove(node, descendant)))
            : ScriptArguments.Wrong(stack, 2, "IsAncestorOf", "Instance", isMethod: true);
    }

    /// <summary>Whether this node is anywhere below the other one. Every node of the level is below game, and every node is below nil.</summary>
    // Nil is Roblox's rule, and so a node that left the level is told from
    // one that is in it by asking about game.
    [ScriptMethod(Parameters = "ancestor: (Instance | DataModel)?", Returns = "boolean")]
    private static int IsDescendantOf(ScriptContext context, LuauStack stack, SceneNode node)
    {
        if (stack.IsNil(2))
            return ScriptResult.Of(stack.Push(true));
        if (context.TryGetObject(stack, 2, out DataModel? game))
            return ScriptResult.Of(stack.Push(NodeTree.IsIn(game.Workspace, node)));

        return context.TryGetObject(stack, 2, out SceneNode? ancestor)
            ? ScriptResult.Of(stack.Push(NodeTree.IsAbove(ancestor, node)))
            : ScriptArguments.Wrong(stack, 2, "IsDescendantOf", "Instance", isMethod: true);
    }

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

    /// <summary>A keyvalue of the entity the node carries, as the level was built with it: a number, a boolean, a string, a Vector3, a Color3 for a colour, or a node for a reference to one. One the builder left alone is what the entity's class gives it. Nil for a name the entity does not have, and for a node with no entity.</summary>
    [ScriptMethod(Parameters = "attribute: string", Returns = "any")]
    private static int GetAttribute(ScriptContext context, LuauStack stack, SceneNode node) =>
        AttributeRead.Push(context, stack, node);

    // The text comes as the bytes Luau keeps, so a search makes no string.
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
