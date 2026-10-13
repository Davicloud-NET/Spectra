using System.Runtime.CompilerServices;
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

    // A destroyed node keeps its attributes and takes more, as in Roblox. It
    // is held weakly: they last as long as anything can hand the node to a
    // script, and go with the node.
    private readonly ConditionalWeakTable<SceneNode, NodeAttributes> _ofDestroyed = new();

    // One string for a name, however many nodes have an attribute of it.
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _namesByChars;
    private int _lookOverAt = FirstLookOver;

    public AttributeTable() => _namesByChars = _names.GetAlternateLookup<ReadOnlySpan<char>>();

    // How many nodes a script set an attribute on, or asked for a signal of.
    // A destroyed node is not counted.
    public int NodeCount => _ofNode.Count;

    // How many destroyed nodes an attribute is still kept for. It walks
    // them all, so it is for a test to ask.
    public int DestroyedCount
    {
        get
        {
            int count = 0;
            foreach (KeyValuePair<SceneNode, NodeAttributes> kept in _ofDestroyed)
            {
                if (kept.Value.Values.Count > 0)
                    count++;
            }

            return count;
        }
    }

    // How many names are kept as the one string nodes share.
    public int NameCount => _names.Count;

    public NodeAttributes? Of(SceneNode node)
    {
        if (_ofNode.TryGetValue(node, out NodeAttributes? own))
            return own;

        return TreeWrites.IsDestroyed(node) && _ofDestroyed.TryGetValue(node, out own) ? own : null;
    }

    public NodeAttributes OfOrMake(SceneNode node)
    {
        if (Of(node) is { } own)
            return own;

        own = new NodeAttributes();
        if (TreeWrites.IsDestroyed(node))
            _ofDestroyed.Add(node, own);
        else
            _ofNode.Add(node, own);

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
        return Of(node) is { } own
            && own.Values.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out value);
    }

    // No value keeps the name as one that was taken away.
    public void Set(SceneNode node, string name, in MemberValue value) => OfOrMake(node).Values[name] = value;

    public void Forget(SceneNode node, ReadOnlySpan<char> name) =>
        Of(node)?.Values.GetAlternateLookup<ReadOnlySpan<char>>().Remove(name);

    // Gives a copy what scripts set on its source, with the names a script
    // took away. The signals stay the source's own.
    public void Copy(SceneNode source, SceneNode copy)
    {
        if (Of(source) is not { Values.Count: > 0 } set)
            return;

        Dictionary<string, MemberValue> values = OfOrMake(copy).Values;
        foreach (KeyValuePair<string, MemberValue> pair in set.Values)
            values[pair.Key] = pair.Value;
    }

    // Takes everything scripts left on a node no script can reach any more,
    // and hands it over, for whoever ends its signals. Null when there is
    // nothing.
    public NodeAttributes? Take(SceneNode node) => _ofNode.Remove(node, out NodeAttributes? own) ? own : null;

    // The node was destroyed. It keeps what scripts set on it. All it had is
    // handed over, for whoever ends its signals. Null when there is nothing.
    public NodeAttributes? Destroyed(SceneNode node)
    {
        if (!_ofNode.Remove(node, out NodeAttributes? own))
            return null;

        if (own.Values.Count > 0)
            _ofDestroyed.AddOrUpdate(node, own);

        return own;
    }

    public void Clear()
    {
        _ofNode.Clear();
        _ofDestroyed.Clear();
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
