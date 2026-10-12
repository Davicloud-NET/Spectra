using System;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// What the catalogue needs of one container: enough to number the names and
// to list the descriptions, and no more, so an edit to a summary leaves the
// catalogue cached. MemberNames are the names that get an atom. HasMethods
// says the container has a method table for the catalogue to build.
internal sealed record ScriptCatalogEntry(
    ScriptContainerKind Kind,
    string FullTypeName,
    string LuauName,
    EquatableArray<string> MemberNames,
    bool HasMethods) : IEquatable<ScriptCatalogEntry>;
