using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What a script can do with a CFrame it holds: read its properties, call its
// methods, and use it with * + - and ==. A CFrame never changes, so nothing
// of it can be set.
internal static class CFrameMembers
{
    public static void Install(LuauHost host)
    {
        MethodTable methods = CFrameMethods.Build(host);

        new ApiTable(host)
            .Function("__index"u8, stack => Index(stack, methods))
            .Function("__newindex"u8, NewIndex)
            .Function("__mul"u8, Multiply)
            .Function("__add"u8, static stack => Offset(stack, 1f))
            .Function("__sub"u8, static stack => Offset(stack, -1f))
            .Function("__eq"u8, AreEqual)
            .Function("__tostring"u8, ToText)
            .Value("__type"u8, "CFrame")
            .Value("__metatable"u8, "The metatable is locked")
            .SetAsMetatableOf(ScriptTags.CFrame);
    }

    private static int Index(LuauStack stack, MethodTable methods)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame frame) || !stack.TryGetUtf8(2, out ReadOnlySpan<byte> name))
            return stack.Fail("a member of a CFrame is named by a string");

        if (TryReadVector(frame, name, out Vector3 vector))
            return ScriptResult.Of(stack.Push(vector));
        if (name.SequenceEqual("Rotation"u8))
            return ScriptResult.Of(FrameValue.Push(stack, new CFrame(Vector3.Zero, frame.Rotation)));
        if (name.Length == 1 && (uint)(name[0] - 'X') < 3)
            return ScriptResult.Of(stack.Push(frame.Position[name[0] - 'X']));

        return methods.TryPush(stack, name)
            ? 1
            : stack.Fail($"{stack.GetText(2)} is not a valid member of CFrame");
    }

    private static bool TryReadVector(CFrame frame, ReadOnlySpan<byte> name, out Vector3 value)
    {
        if (name.SequenceEqual("Position"u8))
            value = frame.Position;
        else if (name.SequenceEqual("LookVector"u8))
            value = frame.LookVector;
        else if (name.SequenceEqual("RightVector"u8) || name.SequenceEqual("XVector"u8))
            value = frame.XVector;
        else if (name.SequenceEqual("UpVector"u8) || name.SequenceEqual("YVector"u8))
            value = frame.YVector;
        else if (name.SequenceEqual("ZVector"u8))
            value = frame.ZVector;
        else
        {
            value = default;
            return false;
        }

        return true;
    }

    private static int NewIndex(LuauStack stack) =>
        stack.Fail($"{stack.GetText(2)} cannot be assigned to");

    // A frame times a frame places the second in the space of the first. A
    // frame times a vector moves the point from the frame's space to the world.
    private static int Multiply(LuauStack stack)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame left))
            return WrongOperands(stack, "mul");
        if (FrameValue.TryGet(stack, 2, out CFrame right))
            return ScriptResult.Of(FrameValue.Push(stack, left * right));

        return stack.TryGetVector(2, out Vector3 point)
            ? ScriptResult.Of(stack.Push(left * point))
            : WrongOperands(stack, "mul");
    }

    private static int Offset(LuauStack stack, float sign)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame frame) || !stack.TryGetVector(2, out Vector3 offset))
            return WrongOperands(stack, sign > 0f ? "add" : "sub");

        return ScriptResult.Of(FrameValue.Push(stack, frame + (offset * sign)));
    }

    private static int AreEqual(LuauStack stack) =>
        ScriptResult.Of(stack.Push(
            FrameValue.TryGet(stack, 1, out CFrame left) && FrameValue.TryGet(stack, 2, out CFrame right) && left == right));

    private static int ToText(LuauStack stack) =>
        FrameValue.TryGet(stack, 1, out CFrame frame)
            ? ScriptResult.Of(stack.Push(frame.ToString()))
            : stack.Fail("this is not a CFrame");

    // The words Luau itself uses for two values an operator cannot take.
    private static int WrongOperands(LuauStack stack, string operation) =>
        stack.Fail(
            $"attempt to perform arithmetic ({operation}) on " +
            $"{ScriptArguments.TypeName(stack, 1)} and {ScriptArguments.TypeName(stack, 2)}");
}
