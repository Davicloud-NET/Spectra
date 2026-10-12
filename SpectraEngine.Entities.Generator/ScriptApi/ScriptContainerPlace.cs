using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// Where a container declares its name, for an error about the name. Kept
// apart from ScriptCatalogEntry: a place moves with every edit above it.
internal sealed record ScriptContainerPlace(
    ScriptContainerKind Kind,
    string LuauName,
    LocationInfo? Location) : IEquatable<ScriptContainerPlace>
{
    // What no two containers may share. Null for globals, which have no
    // name of their own and may be declared by several classes.
    public string? NameKey => NameKeyOf(Kind, LuauName);

    public static string? NameKeyOf(ScriptContainerKind kind, string luauName) => kind switch
    {
        ScriptContainerKind.Globals => null,
        ScriptContainerKind.Library => "library " + luauName,
        _ => "class " + luauName,
    };
}
