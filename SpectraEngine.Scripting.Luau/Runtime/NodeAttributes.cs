using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What scripts left on one node: a value for each name they set, and the
// signals they asked for, each made on the first ask. A name with no value
// is one a script took away from an entity's keyvalues, which a script
// reads as nil from then on.
internal sealed class NodeAttributes
{
    public Dictionary<string, MemberValue> Values { get; } = new(StringComparer.Ordinal);

    // AttributeChanged.
    public ScriptSignal? Changed { get; set; }

    // GetAttributeChangedSignal, by the attribute's name.
    public Dictionary<string, ScriptSignal>? ChangedOf { get; set; }
}
