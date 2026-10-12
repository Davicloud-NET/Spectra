namespace SpectraEngine.Scripting.Luau.Runtime;

// Something a script holds that is not a node: a service, a signal, a
// connection. A class of them carries [ScriptClass], and the generator
// overrides the members below from what the class declares.
internal abstract class ScriptObject
{
    // What an error calls the object's class.
    public abstract string ClassName { get; }

    // What the object prints as.
    public abstract string Name { get; }

    // Each answers the way a host function does, or with one of ScriptMember.
    internal virtual int ReadMember(ScriptContext context, LuauStack stack, int atom) => ScriptMember.None;

    internal virtual int WriteMember(ScriptContext context, LuauStack stack, int atom) => ScriptMember.None;

    internal virtual int InvokeMember(ScriptContext context, LuauStack stack, int atom) => ScriptMember.None;
}
