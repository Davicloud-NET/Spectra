using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The attributes scripts set on nodes, for one play session. They are kept
// here and on no node, so Stop leaves none behind. Nothing here fires a
// signal or knows of Luau, so a level's own attributes can be put in before
// any script runs.
internal sealed class AttributeTable
{
    private readonly Dictionary<SceneNode, NodeAttributes> _ofNode = [];

    // One string for a name, however many nodes have an attribute of it.
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _namesByChars;

    public AttributeTable() => _namesByChars = _names.GetAlternateLookup<ReadOnlySpan<char>>();

    // How many nodes a script set an attribute on, or asked for a signal of.
    public int NodeCount => _ofNode.Count;

    public NodeAttributes? Of(SceneNode node) => _ofNode.GetValueOrDefault(node);

    public NodeAttributes OfOrMake(SceneNode node)
    {
        if (!_ofNode.TryGetValue(node, out NodeAttributes? own))
        {
            own = new NodeAttributes();
            _ofNode.Add(node, own);
        }

        return own;
    }

    // The name as the one string every node shares.
    public string NameOf(ReadOnlySpan<char> name)
    {
        if (_namesByChars.TryGetValue(name, out string? known))
            return known;

        string made = name.ToString();
        _names.Add(made);
        return made;
    }

    // What a script set under the name. True with no value for a name a
    // script took away.
    public bool TryGet(SceneNode node, ReadOnlySpan<char> name, out MemberValue value)
    {
        value = MemberValue.None;
        return _ofNode.TryGetValue(node, out NodeAttributes? own)
            && own.Values.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out value);
    }

    // No value keeps the name as one that was taken away.
    public void Set(SceneNode node, string name, in MemberValue value) => OfOrMake(node).Values[name] = value;

    public void Forget(SceneNode node, ReadOnlySpan<char> name)
    {
        if (_ofNode.TryGetValue(node, out NodeAttributes? own))
            own.Values.GetAlternateLookup<ReadOnlySpan<char>>().Remove(name);
    }

    public void Clear()
    {
        _ofNode.Clear();
        _names.Clear();
    }
}
