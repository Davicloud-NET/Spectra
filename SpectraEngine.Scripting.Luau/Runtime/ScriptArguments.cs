using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// How a host function reads what a script handed it, and what it says when
// the script handed it something else.
internal static class ScriptArguments
{
    public static bool TryNumber(LuauStack stack, int index, out float value)
    {
        bool isNumber = stack.TryGetNumber(index, out double number);
        value = (float)number;
        return isNumber;
    }

    // A number that may be left out, as the up vector of a look-at is.
    public static bool TryOptionalNumber(LuauStack stack, int index, double fallback, out double value)
    {
        value = fallback;
        return stack.IsNil(index) || stack.TryGetNumber(index, out value);
    }

    public static bool TryVector(LuauStack stack, int index, out Vector3 value) =>
        stack.TryGetVector(index, out value);

    public static bool TryOptionalVector(LuauStack stack, int index, Vector3 fallback, out Vector3 value)
    {
        value = fallback;
        return stack.IsNil(index) || stack.TryGetVector(index, out value);
    }

    public static bool TryFrame(LuauStack stack, int index, out CFrame value) =>
        FrameValue.TryGet(stack, index, out value);

    // The readers generated glue calls, one for each kind of value. A
    // string that reads as a number counts as one, as it does in Luau.
    public static bool TryDouble(LuauStack stack, int index, out double value) =>
        stack.TryGetNumber(index, out value);

    // True and false only. Any value counts as one of them in a condition,
    // and an argument of the wrong kind is a mistake.
    public static bool TryBoolean(LuauStack stack, int index, out bool value)
    {
        bool isBoolean = stack.TypeOf(index) == LuauType.Boolean;
        value = isBoolean && stack.GetBoolean(index);
        return isBoolean;
    }

    // Makes a string. A function that is called every tick takes the bytes.
    public static bool TryText(LuauStack stack, int index, [NotNullWhen(true)] out string? value)
    {
        value = stack.GetText(index);
        return value is not null;
    }

    // The bytes where Luau keeps them. They live while the string stays on the stack.
    public static bool TryUtf8(LuauStack stack, int index, out ReadOnlySpan<byte> value) =>
        stack.TryGetUtf8(index, out value);

    // The error for an argument of the wrong kind, in Roblox's words. A method
    // counts its arguments from the one after the object.
    public static int Wrong(LuauStack stack, int index, string function, string expected, bool isMethod = false)
    {
        int number = isMethod ? index - 1 : index;
        return stack.Fail(
            $"invalid argument #{number} to '{function}' ({expected} expected, got {TypeName(stack, index)})");
    }

    // The error for a method called with a dot, so that it got no object.
    public static int NoSelf(LuauStack stack, string method) =>
        stack.Fail($"Expected ':' not '.' calling member function {method}");

    // What a script would call the kind of value at the index.
    public static string TypeName(LuauStack stack, int index) => stack.TypeOf(index) switch
    {
        LuauType.None or LuauType.Nil => "nil",
        LuauType.Boolean => "boolean",
        LuauType.Number or LuauType.Integer => "number",
        LuauType.Vector => "Vector3",
        LuauType.String => "string",
        LuauType.Table => "table",
        LuauType.Function => "function",
        LuauType.Thread => "thread",
        LuauType.Buffer => "buffer",
        _ => FrameValue.TryGet(stack, index, out _) ? "CFrame" : "Instance",
    };
}
