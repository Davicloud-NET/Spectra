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

    // Ends every listener of the node's signals and lets the signals go, for
    // a node that fires nothing more. The values stay.
    public void EndSignals()
    {
        // Out first: ending a thread can run a script that asks for a signal.
        ScriptSignal? changed = Changed;
        Dictionary<string, ScriptSignal>? byName = ChangedOf;
        Changed = null;
        ChangedOf = null;

        changed?.EndAll();
        if (byName is null)
            return;

        foreach (ScriptSignal signal in byName.Values)
            signal.EndAll();
    }
}
