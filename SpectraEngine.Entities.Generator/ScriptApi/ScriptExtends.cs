using System.Collections.Generic;

namespace SpectraEngine.Entities.Generator.ScriptApi;

// Finds the class a class extends. The binding of a class hands a member it
// does not declare to that class, so it must be one the generated code can
// call with the same object.
internal static class ScriptExtends
{
    // The nearest class above the entry that has a binding, or null. `why`
    // is set when the entry names a class the binding cannot follow.
    public static ScriptCatalogEntry? BaseOf(
        ScriptCatalogEntry entry, IReadOnlyList<ScriptCatalogEntry> listed, out string? why)
    {
        why = null;
        if (IsEndless(entry, listed))
        {
            why = "the classes it extends go round in a circle";
            return null;
        }

        ScriptCatalogEntry at = entry;
        while (at.Extends.Length > 0)
        {
            if (ClassNamed(at.Extends, listed) is not { } above)
            {
                why = $"'{at.Extends}' is no class declared with [ScriptClass]";
                return null;
            }

            at = above;
            if (at.SelfType.Length == 0 || entry.SelfType.Length == 0)
                continue;

            if (at.SelfType == entry.SelfType)
                return at;

            why = $"the members of '{at.LuauName}' are of another object, so the binding cannot hand them on";
            return null;
        }

        return null;
    }

    // A line of classes longer than there are classes comes back to one of them.
    private static bool IsEndless(ScriptCatalogEntry entry, IReadOnlyList<ScriptCatalogEntry> listed)
    {
        ScriptCatalogEntry? at = entry;
        for (int depth = 0; at is { Extends.Length: > 0 }; depth++)
        {
            if (depth > listed.Count)
                return true;

            at = ClassNamed(at.Extends, listed);
        }

        return false;
    }

    private static ScriptCatalogEntry? ClassNamed(string name, IReadOnlyList<ScriptCatalogEntry> listed)
    {
        foreach (ScriptCatalogEntry candidate in listed)
        {
            if (candidate.Kind == ScriptContainerKind.Class && candidate.LuauName == name)
                return candidate;
        }

        return null;
    }
}
