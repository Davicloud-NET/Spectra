using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The attributes scripts set on nodes, for one play session. They are kept
// here and on no node, so Stop leaves none behind. Nothing here fires a
// signal or knows of Luau, so a level's own attributes can be put in before
// any script runs.
internal sealed class AttributeTable
{
    // With fewer names than this none is looked over.
    private const int FirstLookOver = 64;

    private readonly Dictionary<SceneNode, NodeAttributes> _ofNode = [];

    // One string for a name, however many nodes have an attribute of it.
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _namesByChars;
    private int _lookOverAt = FirstLookOver;

    public AttributeTable() => _namesByChars = _names.GetAlternateLookup<ReadOnlySpan<char>>();

    // How many nodes a script set an attribute on, or asked for a signal of.
    public int NodeCount => _ofNode.Count;

    // How many names are kept as the one string nodes share.
    public int NameCount => _names.Count;

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

        if (_names.Count >= _lookOverAt)
            LetUnusedNamesGo();

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

    // A name stays when its last attribute is taken away. So each time there
    // are twice as many as were in use the last time, the names no node has
    // an attribute or a signal of are let go.
    private void LetUnusedNamesGo()
    {
        _names.Clear();
        foreach (NodeAttributes own in _ofNode.Values)
        {
            foreach (string name in own.Values.Keys)
                _names.Add(name);

            if (own.ChangedOf is not { } signals)
                continue;

            foreach (string name in signals.Keys)
                _names.Add(name);
        }

        _lookOverAt = Math.Max(FirstLookOver, _names.Count * 2);
    }
}
