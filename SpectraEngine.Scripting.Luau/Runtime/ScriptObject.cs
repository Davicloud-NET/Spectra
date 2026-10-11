namespace SpectraEngine.Scripting.Luau.Runtime;

// Something a script holds that is not a node: a service, a signal, a
// connection. Nothing of one can be set from a script.
internal abstract class ScriptObject
{
    // What ReadProperty answers for a name the object has no property of.
    public const int NoSuchProperty = 0;

    // What the object's ClassName would read, and what an error calls it.
    public abstract string ClassName { get; }

    // What the object prints as.
    public abstract string Name { get; }

    // The methods of its kind, or null when it has none.
    public virtual MethodTable? Methods => null;

    // Answers a read the way a host function does: one value pushed, or a
    // failure. NoSuchProperty when the name is no property of the object.
    public virtual int ReadProperty(LuauStack stack, ReadOnlySpan<byte> name) => NoSuchProperty;
}
