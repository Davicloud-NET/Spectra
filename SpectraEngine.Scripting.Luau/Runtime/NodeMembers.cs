using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What a script reads and writes on a node. A write moves the node through
// the entity world, which keeps where it was authored, so Stop puts it back.
internal sealed class NodeMembers(LuauHost host, EntityWorld world)
{
    public int Read(LuauStack stack, SceneNode node, ReadOnlySpan<byte> name)
    {
        if (name.SequenceEqual("Name"u8))
            return ScriptResult.Of(stack.Push(node.Name));
        if (name.SequenceEqual("ClassName"u8))
            return ScriptResult.Of(stack.Push(NodeClassName.Of(node)));
        if (name.SequenceEqual("Parent"u8))
            return PushNode(stack, node.Parent);
        if (name.SequenceEqual("Position"u8))
            return ScriptResult.Of(stack.Push(node.WorldPosition));
        if (name.SequenceEqual("CFrame"u8))
            return ScriptResult.Of(FrameValue.Push(stack, node.CFrame));
        if (name.SequenceEqual("LocalPosition"u8))
            return ScriptResult.Of(stack.Push(node.LocalPosition));
        if (name.SequenceEqual("LocalCFrame"u8))
            return ScriptResult.Of(FrameValue.Push(stack, node.LocalCFrame));

        return stack.Fail(NotAMember(stack, node));
    }

    // The value to write is the third argument of __newindex.
    public int Write(LuauStack stack, SceneNode node, ReadOnlySpan<byte> name)
    {
        if (name.SequenceEqual("Position"u8))
        {
            return stack.TryGetVector(3, out Vector3 position)
                ? MoveTo(stack, node, new CFrame(position, node.CFrame.Rotation))
                : WrongValue(stack, "Position", "Vector3");
        }

        if (name.SequenceEqual("CFrame"u8))
        {
            return FrameValue.TryGet(stack, 3, out CFrame frame)
                ? MoveTo(stack, node, frame)
                : WrongValue(stack, "CFrame", "CFrame");
        }

        if (name.SequenceEqual("LocalPosition"u8))
        {
            return stack.TryGetVector(3, out Vector3 position)
                ? Move(stack, node, node.LocalTransform with { Position = position })
                : WrongValue(stack, "LocalPosition", "Vector3");
        }

        if (name.SequenceEqual("LocalCFrame"u8))
        {
            return FrameValue.TryGet(stack, 3, out CFrame frame)
                ? Move(stack, node, LocalAt(node, frame))
                : WrongValue(stack, "LocalCFrame", "CFrame");
        }

        return stack.Fail(WhyNotWritable(stack, node, name));
    }

    public int PushNode(LuauStack stack, SceneNode? node) =>
        ScriptResult.Of(node is null ? stack.PushNil() : host.Objects.Push(stack, ScriptTags.Object, node));

    // The node's own setter leaves a frame the node already has alone, since
    // solving it again comes back a rounding error away.
    private int MoveTo(LuauStack stack, SceneNode node, CFrame world) =>
        Move(stack, node, world == node.CFrame ? node.LocalTransform : node.LocalTransformAt(world));

    private static Transform LocalAt(SceneNode node, CFrame local) =>
        local == node.LocalCFrame
            ? node.LocalTransform
            : node.LocalTransform with { Position = local.Position, Rotation = local.Rotation };

    private int Move(LuauStack stack, SceneNode node, in Transform local)
    {
        if (!world.IsActive)
            return stack.Fail($"{node.Name} cannot be moved: the level has stopped.");

        // A node at such a place is in no cell of the scene's index, and no
        // query finds it again. A rotation is brought to unit length when a
        // CFrame is made, so only the position can be off.
        Vector3 position = local.Position;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            return stack.Fail($"{node.Name} cannot be moved: a position must be three numbers, with none of them infinite.");

        return world.TrySetLocalTransform(node, in local, out string? refusal)
            ? 0
            : stack.Fail($"{node.Name} cannot be moved: {refusal}.");
    }

    private static string WhyNotWritable(LuauStack stack, SceneNode node, ReadOnlySpan<byte> name)
    {
        if (name.SequenceEqual("ClassName"u8))
            return "Unable to assign property ClassName. Property is read only";
        if (name.SequenceEqual("Name"u8) || name.SequenceEqual("Parent"u8))
            return $"{stack.GetText(2)} cannot be set from a script yet";

        return NotAMember(stack, node);
    }

    // Roblox's words for a name a class does not have.
    private static string NotAMember(LuauStack stack, SceneNode node) =>
        $"{stack.GetText(2)} is not a valid member of {NodeClassName.Of(node)} \"{node.Name}\"";

    private static int WrongValue(LuauStack stack, string property, string expected) =>
        stack.Fail(
            $"Unable to assign property {property}. {expected} expected, got {ScriptArguments.TypeName(stack, 3)}");
}
