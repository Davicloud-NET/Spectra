using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>Finds the nodes of the level by their tags, and tells of a node that gets or loses one. A tag is a word on a node: one it was built with in the editor, or one a script puts on it while the level plays. Stop puts every node's tags back as they were built.</summary>
// Every method is written by hand: each reads a tag where Luau keeps it, and
// the node's own AddTag, RemoveTag, HasTag and GetTags share the code.
[ScriptClass("CollectionService")]
internal sealed partial class CollectionService : ScriptObject
{
    private readonly TagMethods _tags;

    public CollectionService(TagMethods tags) => _tags = tags;

    public override string Name => "CollectionService";

    /// <summary>The nodes of the level with that tag, in the order of the tree. A node that left the level is not among them.</summary>
    [ScriptMethod(Parameters = "tag: string", Returns = "{Instance}")]
    private int GetTagged(ScriptContext context, LuauStack stack) => _tags.PushTagged(context, stack, tagAt: 2);

    /// <summary>The signal that fires with the node when a node of the level gets that tag, and when a node with the tag comes into the level.</summary>
    [ScriptMethod(Parameters = "tag: string", Returns = "RBXScriptSignal<Instance>")]
    private int GetInstanceAddedSignal(ScriptContext context, LuauStack stack) =>
        _tags.PushSignal(context, stack, tagAt: 2, isOfAdded: true);

    /// <summary>The signal that fires with the node when a node of the level loses that tag, and when a node with the tag leaves the level.</summary>
    [ScriptMethod(Parameters = "tag: string", Returns = "RBXScriptSignal<Instance>")]
    private int GetInstanceRemovedSignal(ScriptContext context, LuauStack stack) =>
        _tags.PushSignal(context, stack, tagAt: 2, isOfAdded: false);

    /// <summary>Puts the tag on the node, as the node's own AddTag does. Nothing happens when the node has it.</summary>
    [ScriptMethod(Parameters = "instance: Instance, tag: string")]
    private int AddTag(ScriptContext context, LuauStack stack) =>
        context.TryGetObject(stack, 2, out SceneNode? node) ? _tags.Add(stack, node, tagAt: 3) : NoNode(stack, "AddTag");

    /// <summary>Takes the tag off the node, as the node's own RemoveTag does. Nothing happens when the node lacks it.</summary>
    [ScriptMethod(Parameters = "instance: Instance, tag: string")]
    private int RemoveTag(ScriptContext context, LuauStack stack) =>
        context.TryGetObject(stack, 2, out SceneNode? node) ? _tags.Remove(stack, node, tagAt: 3) : NoNode(stack, "RemoveTag");

    /// <summary>Whether the node has the tag.</summary>
    [ScriptMethod(Parameters = "instance: Instance, tag: string", Returns = "boolean")]
    private int HasTag(ScriptContext context, LuauStack stack) =>
        context.TryGetObject(stack, 2, out SceneNode? node) ? _tags.Has(stack, node, tagAt: 3) : NoNode(stack, "HasTag");

    /// <summary>The node's tags, in the order it got them.</summary>
    [ScriptMethod(Parameters = "instance: Instance", Returns = "{string}")]
    private int GetTags(ScriptContext context, LuauStack stack) =>
        context.TryGetObject(stack, 2, out SceneNode? node) ? _tags.PushTagsOf(stack, node) : NoNode(stack, "GetTags");

    private static int NoNode(LuauStack stack, string method) =>
        ScriptArguments.Wrong(stack, 2, method, "Instance", isMethod: true);
}
