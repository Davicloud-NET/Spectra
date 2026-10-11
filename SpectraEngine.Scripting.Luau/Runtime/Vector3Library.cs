using System.Numerics;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Vector3 as a script sees it, on Luau's own vector type. The arithmetic and
// the three components are Luau's. What is here is the constructor, the
// constants and the members Roblox adds.
internal static class Vector3Library
{
    public static void Install(LuauHost host)
    {
        new ApiTable(host)
            .Function("new"u8, New)
            .Value("zero"u8, Vector3.Zero)
            .Value("one"u8, Vector3.One)
            .Value("xAxis"u8, Vector3.UnitX)
            .Value("yAxis"u8, Vector3.UnitY)
            .Value("zAxis"u8, Vector3.UnitZ)
            .SetAsGlobal("Vector3"u8);

        MethodTable methods = new ApiTable(host)
            .Function("Dot"u8, Dot)
            .Function("Cross"u8, Cross)
            .Function("Lerp"u8, Lerp)
            .KeepAsMethods();

        new ApiTable(host)
            .Function("__index"u8, stack => Index(stack, methods))
            .Value("__type"u8, "Vector3")
            .Value("__metatable"u8, "The metatable is locked")
            .SetAsVectorMetatable();
    }

    // The compiler makes the vector itself when it sees three numbers. This
    // is for every other call: fewer numbers, or the function passed around.
    private static int New(LuauStack stack)
    {
        Span<float> parts = [0f, 0f, 0f];
        for (int index = 1; index <= parts.Length; index++)
        {
            if (!ScriptArguments.TryOptionalNumber(stack, index, 0, out double part))
                return ScriptArguments.Wrong(stack, index, "new", "number");

            parts[index - 1] = (float)part;
        }

        return ScriptResult.Of(stack.Push(new Vector3(parts)));
    }

    private static int Index(LuauStack stack, MethodTable methods)
    {
        if (!stack.TryGetVector(1, out Vector3 vector) || !stack.TryGetUtf8(2, out ReadOnlySpan<byte> name))
            return stack.Fail("a member of a Vector3 is named by a string");

        if (name.SequenceEqual("Magnitude"u8))
            return ScriptResult.Of(stack.Push(vector.Length()));
        if (name.SequenceEqual("Unit"u8))
            return ScriptResult.Of(stack.Push(Vector3.Normalize(vector)));

        // Luau reads a component with no call when the name is written out.
        // This is the same for a name that was worked out, as in v[axis].
        if (name.Length == 1 && (uint)((name[0] | 0x20) - 'x') < 3)
            return ScriptResult.Of(stack.Push(vector[(name[0] | 0x20) - 'x']));

        return methods.TryPush(stack, name)
            ? 1
            : stack.Fail($"{stack.GetText(2)} is not a valid member of Vector3");
    }

    private static int Dot(LuauStack stack)
    {
        if (!stack.TryGetVector(1, out Vector3 self))
            return ScriptArguments.NoSelf(stack, "Dot");
        if (!stack.TryGetVector(2, out Vector3 other))
            return ScriptArguments.Wrong(stack, 2, "Dot", "Vector3", isMethod: true);

        return ScriptResult.Of(stack.Push(Vector3.Dot(self, other)));
    }

    private static int Cross(LuauStack stack)
    {
        if (!stack.TryGetVector(1, out Vector3 self))
            return ScriptArguments.NoSelf(stack, "Cross");
        if (!stack.TryGetVector(2, out Vector3 other))
            return ScriptArguments.Wrong(stack, 2, "Cross", "Vector3", isMethod: true);

        return ScriptResult.Of(stack.Push(Vector3.Cross(self, other)));
    }

    private static int Lerp(LuauStack stack)
    {
        if (!stack.TryGetVector(1, out Vector3 self))
            return ScriptArguments.NoSelf(stack, "Lerp");
        if (!stack.TryGetVector(2, out Vector3 goal))
            return ScriptArguments.Wrong(stack, 2, "Lerp", "Vector3", isMethod: true);
        if (!ScriptArguments.TryNumber(stack, 3, out float alpha))
            return ScriptArguments.Wrong(stack, 3, "Lerp", "number", isMethod: true);

        return ScriptResult.Of(stack.Push(Vector3.Lerp(self, goal, alpha)));
    }
}
