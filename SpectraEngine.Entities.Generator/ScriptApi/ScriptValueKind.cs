namespace SpectraEngine.Entities.Generator.ScriptApi;

// How a C# value crosses into Luau and back.
internal enum ScriptValueKind : byte
{
    Number,
    Boolean,
    Text,

    // ReadOnlySpan<byte>: a string read where Luau keeps it. Arguments only.
    Utf8,

    Vector3,
    CFrame,

    // A node, or an object of a class with [ScriptClass].
    Object,
}
