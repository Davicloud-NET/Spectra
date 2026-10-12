namespace SpectraEngine.Scripting.Luau.Runtime;

// What generated glue answers in place of a result count when it has no
// result: the member's atom meant nothing to the class, or not this. The
// metatable that asked puts it into the words a script reads.
internal static class ScriptMember
{
    // The class has no member of that name.
    public const int None = int.MinValue;

    // A write to a member a script cannot set.
    public const int ReadOnly = int.MinValue + 1;

    // A call of a member that is a property.
    public const int NotCallable = int.MinValue + 2;

    // Luau's own words for a call of what is no function. The value that
    // was called is on top of the stack.
    public static int FailNotCallable(LuauStack stack) =>
        stack.Fail($"attempt to call a {ScriptArguments.TypeName(stack, -1)} value");
}
