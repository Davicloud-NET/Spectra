using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The metatable of a Color3 a script holds: what a read, a call and a
// comparison come to. The members are Color3Api's. A Color3 never changes,
// so nothing of it can be set, and it takes no arithmetic, as in Roblox.
internal static class Color3Members
{
    public static void Install(ScriptContext context) =>
        new ApiTable(context.Host)
            .Function("__index"u8, stack => Index(context, stack))
            .Function("__newindex"u8, NewIndex)
            .Function("__namecall"u8, stack => NameCall(context, stack))
            .Function("__eq"u8, AreEqual)
            .Function("__tostring"u8, ToText)
            .Value("__type"u8, "Color3")
            .Value("__metatable"u8, "The metatable is locked")
            .SetAsMetatableOf(ScriptTags.Color3);

    private static int Index(ScriptContext context, LuauStack stack)
    {
        if (!ColorValue.TryGet(stack, 1, out Color3 color) || !stack.TryGetAtom(2, out int atom))
            return stack.Fail("a member of a Color3 is named by a string");

        int read = Color3Api.ScriptRead(context, stack, color, atom);
        return read == ScriptMember.None ? stack.Fail($"{stack.GetText(2)} is not a valid member of Color3") : read;
    }

    private static int NameCall(ScriptContext context, LuauStack stack)
    {
        if (!ColorValue.TryGet(stack, 1, out Color3 color))
            return stack.Fail("this is not a Color3");

        int atom = stack.NamecallAtom;
        int called = Color3Api.ScriptInvoke(context, stack, color, atom);
        if (called == ScriptMember.None)
            return stack.Fail($"{stack.NamecallName} is not a valid member of Color3");
        if (called != ScriptMember.NotCallable)
            return called;

        int read = Color3Api.ScriptRead(context, stack, color, atom);
        return read == 1 ? ScriptMember.FailNotCallable(stack) : read;
    }

    private static int NewIndex(LuauStack stack) =>
        stack.Fail($"{stack.GetText(2)} cannot be assigned to");

    private static int AreEqual(LuauStack stack) =>
        ScriptResult.Of(stack.Push(
            ColorValue.TryGet(stack, 1, out Color3 left) && ColorValue.TryGet(stack, 2, out Color3 right) && left == right));

    private static int ToText(LuauStack stack) =>
        ColorValue.TryGet(stack, 1, out Color3 color)
            ? ScriptResult.Of(stack.Push(color.ToString()))
            : stack.Fail("this is not a Color3");
}
