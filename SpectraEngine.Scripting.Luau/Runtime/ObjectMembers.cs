using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The one metatable every managed object shares: what a read, a write and a
// print of a node, a service, a signal or a connection come to. A name the
// object does not have is an error, never nil.
internal sealed class ObjectMembers
{
    private const string HasLeft = "this object has left the level";

    private readonly LuauHost _host;
    private readonly NodeMembers _nodes;

    private ObjectMembers(LuauHost host, NodeMembers nodes)
    {
        _host = host;
        _nodes = nodes;
    }

    public static void Install(LuauHost host, NodeMembers nodes)
    {
        ObjectMembers members = new(host, nodes);

        new ApiTable(host)
            .Function("__index"u8, members.Index)
            .Function("__newindex"u8, members.NewIndex)
            .Function("__tostring"u8, members.ToText)
            .Value("__type"u8, "Instance")
            .Value("__metatable"u8, "The metatable is locked")
            .SetAsObjectMetatable(ScriptTags.Object);
    }

    private int Index(LuauStack stack)
    {
        if (TargetOf(stack) is not { } target)
            return stack.Fail(HasLeft);
        if (!stack.TryGetUtf8(2, out ReadOnlySpan<byte> name))
            return stack.Fail(NotAName(stack));
        if (target is SceneNode node)
            return _nodes.Read(stack, node, name);

        ScriptObject other = (ScriptObject)target;
        int read = other.ReadProperty(stack, name);
        if (read != ScriptObject.NoSuchProperty)
            return read;

        return other.Methods is { } methods && methods.TryPush(stack, name)
            ? 1
            : stack.Fail(NotAMember(stack, other));
    }

    private int NewIndex(LuauStack stack)
    {
        if (TargetOf(stack) is not { } target)
            return stack.Fail(HasLeft);
        if (!stack.TryGetUtf8(2, out ReadOnlySpan<byte> name))
            return stack.Fail(NotAName(stack));
        if (target is SceneNode node)
            return _nodes.Write(stack, node, name);

        ScriptObject other = (ScriptObject)target;
        bool isMember = other.ReadProperty(stack, name) != ScriptObject.NoSuchProperty
            || (other.Methods is { } methods && methods.TryPush(stack, name));

        return stack.Fail(isMember
            ? $"Unable to assign property {stack.GetText(2)}. Property is read only"
            : NotAMember(stack, other));
    }

    private int ToText(LuauStack stack) =>
        ScriptResult.Of(stack.Push(TargetOf(stack) switch
        {
            SceneNode node => node.Name,
            ScriptObject other => other.Name,
            _ => "an object that has left the level",
        }));

    // Null for an object that has left the level, and for what is no object.
    private object? TargetOf(LuauStack stack) =>
        _host.Objects.TryGet(stack, 1, ScriptTags.Object, out object? target) == LuauObjectLookup.Found
            ? target
            : null;

    private static string NotAName(LuauStack stack) =>
        $"a member is named by a string, not by a {ScriptArguments.TypeName(stack, 2)}";

    private static string NotAMember(LuauStack stack, ScriptObject target) =>
        $"{stack.GetText(2)} is not a valid member of {target.ClassName} \"{target.Name}\"";
}
