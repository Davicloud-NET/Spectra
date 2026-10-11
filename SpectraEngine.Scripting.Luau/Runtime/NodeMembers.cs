using System.Numerics;
using System.Text;
using System.Text.Unicode;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What a script reads and writes on a node. ScriptWrites is the list of what
// can be set and the only thing here that sets it, so Stop puts every write
// back. Nothing in the binding changes a node itself.
internal sealed class NodeMembers(LuauHost host, ScriptWrites writes)
{
    public int Read(LuauStack stack, SceneNode node, ReadOnlySpan<byte> name)
    {
        if (ScriptWrites.TryFind(name, out SettableMember member))
            return Push(stack, ScriptWrites.Read(node, member.Id));
        if (name.SequenceEqual("ClassName"u8))
            return ScriptResult.Of(stack.Push(NodeClassName.Of(node)));
        if (name.SequenceEqual("Parent"u8))
            return PushNode(stack, node.Parent);

        return stack.Fail(NotAMember(stack, node));
    }

    // The value to write is the third argument of __newindex.
    public int Write(LuauStack stack, SceneNode node, ReadOnlySpan<byte> name)
    {
        if (!ScriptWrites.TryFind(name, out SettableMember member))
            return stack.Fail(WhyNotWritable(stack, node, name));
        if (ReadValue(stack, 3, in member, out MemberValue value) is { } wrong)
            return stack.Fail(wrong);

        return writes.TryWrite(node, member.Id, in value, out string? refusal) ? 0 : stack.Fail(refusal);
    }

    public int PushNode(LuauStack stack, SceneNode? node) =>
        ScriptResult.Of(node is null ? stack.PushNil() : host.Objects.Push(stack, ScriptTags.Object, node));

    private static int Push(LuauStack stack, in MemberValue value) => ScriptResult.Of(value.Kind switch
    {
        MemberValueKind.Text => stack.Push(value.Text),
        MemberValueKind.Vector3 => stack.Push(value.Vector),
        MemberValueKind.CFrame => FrameValue.Push(stack, value.Frame),
        _ => stack.PushNil(),
    });

    // Null when the value is of the kind the member takes. Otherwise what to
    // tell the script.
    private static string? ReadValue(LuauStack stack, int index, in SettableMember member, out MemberValue value)
    {
        value = default;
        switch (member.Kind)
        {
            case MemberValueKind.Vector3 when stack.TryGetVector(index, out Vector3 vector):
                value = MemberValue.Of(vector);
                return null;

            case MemberValueKind.CFrame when FrameValue.TryGet(stack, index, out CFrame frame):
                value = MemberValue.Of(frame);
                return null;

            case MemberValueKind.Text when stack.TryGetUtf8(index, out ReadOnlySpan<byte> text):
                // A string of Luau is bytes. Bytes that are no text would
                // read back as other bytes than the script wrote.
                if (!Utf8.IsValid(text))
                    return $"Unable to assign property {member.Name}. The string is not valid UTF-8";

                value = MemberValue.Of(Encoding.UTF8.GetString(text));
                return null;

            default:
                return ScriptWrites.WrongKind(in member, ScriptArguments.TypeName(stack, index));
        }
    }

    private static string WhyNotWritable(LuauStack stack, SceneNode node, ReadOnlySpan<byte> name)
    {
        if (name.SequenceEqual("ClassName"u8))
            return "Unable to assign property ClassName. Property is read only";
        if (name.SequenceEqual("Parent"u8))
            return "Parent cannot be set from a script yet";

        return NotAMember(stack, node);
    }

    // Roblox's words for a name a class does not have.
    private static string NotAMember(LuauStack stack, SceneNode node) =>
        $"{stack.GetText(2)} is not a valid member of {NodeClassName.Of(node)} \"{node.Name}\"";
}
