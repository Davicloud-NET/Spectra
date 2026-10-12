using System;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// What the catalogue needs of one container: enough to number the names,
// to list the descriptions and to find the class one extends. No more, so
// an edit to a summary leaves the catalogue cached. MemberNames are the
// names that get an atom. SelfType is empty for a class with no binding.
internal sealed record ScriptCatalogEntry(
    ScriptContainerKind Kind,
    string FullTypeName,
    string LuauName,
    string Extends,
    string SelfType,
    EquatableArray<string> MemberNames,
    bool HasMethods) : IEquatable<ScriptCatalogEntry>
{
    // What no two containers may share. Null for globals, which have no
    // name of their own and may be declared by several classes.
    public string? NameKey => Kind switch
    {
        ScriptContainerKind.Globals => null,
        ScriptContainerKind.Library => "library " + LuauName,
        _ => "class " + LuauName,
    };

    // The entries the catalogue lists: the first under each name.
    public static List<ScriptCatalogEntry> Listed(IEnumerable<ScriptCatalogEntry> entries)
    {
        var listed = new List<ScriptCatalogEntry>();
        var seen = new HashSet<string>();
        foreach (ScriptCatalogEntry entry in entries)
        {
            if (entry.NameKey is not { } key || seen.Add(key))
                listed.Add(entry);
        }

        return listed;
    }
}
