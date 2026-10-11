namespace SpectraEngine.Scripting.Luau.Runtime;

// What a host function returns after it pushed its one result.
internal static class ScriptResult
{
    // A push that failed has left its reason on top, to be raised as it is.
    public static int Of(bool isPushed) => isPushed ? 1 : LuauStack.Raise;
}
