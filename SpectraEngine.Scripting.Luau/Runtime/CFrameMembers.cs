using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The metatable of a CFrame a script holds: what a read, a call and an
// operator come to. The members are CFrameApi's. A CFrame never changes,
// so nothing of it can be set.
internal static class CFrameMembers
{
    public static void Install(ScriptContext context) =>
        new ApiTable(context.Host)
            .Function("__index"u8, stack => Index(context, stack))
            .Function("__newindex"u8, NewIndex)
            .Function("__namecall"u8, stack => NameCall(context, stack))
            .Function("__mul"u8, CFrameApi.Multiply)
            .Function("__add"u8, CFrameApi.Add)
            .Function("__sub"u8, CFrameApi.Subtract)
            .Function("__eq"u8, AreEqual)
            .Function("__tostring"u8, ToText)
            .Value("__type"u8, "CFrame")
            .Value("__metatable"u8, "The metatable is locked")
            .SetAsMetatableOf(ScriptTags.CFrame);

    private static int Index(ScriptContext context, LuauStack stack)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame frame) || !stack.TryGetAtom(2, out int atom))
            return stack.Fail("a member of a CFrame is named by a string");

        int read = CFrameApi.ScriptRead(context, stack, frame, atom);
        return read == ScriptMember.None ? stack.Fail($"{stack.GetText(2)} is not a valid member of CFrame") : read;
    }

    private static int NameCall(ScriptContext context, LuauStack stack)
    {
        if (!FrameValue.TryGet(stack, 1, out CFrame frame))
            return stack.Fail("this is not a CFrame");

        int atom = stack.NamecallAtom;
        int called = CFrameApi.ScriptInvoke(context, stack, frame, atom);
        if (called == ScriptMember.None)
            return stack.Fail($"{stack.NamecallName} is not a valid member of CFrame");
        if (called != ScriptMember.NotCallable)
            return called;

        int read = CFrameApi.ScriptRead(context, stack, frame, atom);
        return read == 1 ? ScriptMember.FailNotCallable(stack) : read;
    }

    private static int NewIndex(LuauStack stack) =>
        stack.Fail($"{stack.GetText(2)} cannot be assigned to");

    private static int AreEqual(LuauStack stack) =>
        ScriptResult.Of(stack.Push(
            FrameValue.TryGet(stack, 1, out CFrame left) && FrameValue.TryGet(stack, 2, out CFrame right) && left == right));

    private static int ToText(LuauStack stack) =>
        FrameValue.TryGet(stack, 1, out CFrame frame)
            ? ScriptResult.Of(stack.Push(frame.ToString()))
            : stack.Fail("this is not a CFrame");
}
