using System.Numerics;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The metatable of every vector, in place of the one Luau's vector library
// set. It is how a Vector3 gets the members Roblox adds, which are
// Vector3Api's.
internal static class Vector3Members
{
    public static void Install(ScriptContext context) =>
        new ApiTable(context.Host)
            .Function("__index"u8, stack => Index(context, stack))
            .Function("__namecall"u8, stack => NameCall(context, stack))
            .Value("__type"u8, "Vector3")
            .Value("__metatable"u8, "The metatable is locked")
            .SetAsVectorMetatable();

    private static int Index(ScriptContext context, LuauStack stack)
    {
        if (!stack.TryGetVector(1, out Vector3 vector) || !stack.TryGetAtom(2, out int atom))
            return stack.Fail("a member of a Vector3 is named by a string");

        int read = Vector3Api.ScriptRead(context, stack, vector, atom);
        return read == ScriptMember.None ? stack.Fail($"{stack.GetText(2)} is not a valid member of Vector3") : read;
    }

    private static int NameCall(ScriptContext context, LuauStack stack)
    {
        if (!stack.TryGetVector(1, out Vector3 vector))
            return stack.Fail("this is not a Vector3");

        int atom = stack.NamecallAtom;
        int called = Vector3Api.ScriptInvoke(context, stack, vector, atom);
        if (called == ScriptMember.None)
            return stack.Fail($"{stack.NamecallName} is not a valid member of Vector3");
        if (called != ScriptMember.NotCallable)
            return called;

        int read = Vector3Api.ScriptRead(context, stack, vector, atom);
        return read == 1 ? ScriptMember.FailNotCallable(stack) : read;
    }
}
