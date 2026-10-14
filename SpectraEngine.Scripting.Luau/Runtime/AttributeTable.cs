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
    private readonly NodeBytes _bytes;
    private int _lookOverAt = FirstLookOver;

    public AttributeTable()
        : this(new NodeBytes(null))
    {
    }

    // What scripts set on nodes that are not destroyed is added to `bytes`.
    public AttributeTable(NodeBytes bytes)
    {
        _bytes = bytes;
        _namesByChars = _names.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    // What scripts set on the nodes that are not destroyed weighs, by
    // NodeWeight. What a node was built with weighs nothing.
    public long Bytes { get; private set; }

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
                if (kept.Value.Count > 0)
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
        return Of(node) is { } own && own.TryGet(name, out value);
    }

    // No value keeps the name as one that was taken away. Hands back by how
    // many bytes the table grew.
    public long Set(SceneNode node, string name, in MemberValue value)
    {
        // A destroyed node is weighed by HeldNodes, while a script holds it.
        bool isCounted = !TreeWrites.IsDestroyed(node);
        if (!_ofNode.TryGetValue(node, out NodeAttributes? own))
        {
            own = isCounted ? new NodeAttributes() : OfOrMake(node);
            if (isCounted)
                _ofNode.Add(node, own);
        }
        else
        {
            isCounted = true;
        }

        bool had = own.Set(name, in value, out MemberValue old);
        if (!isCounted)
            return 0;

        long grown = NodeWeight.OfSetAttribute(node, name, in value);
        if (had)
            grown -= NodeWeight.OfSetAttribute(node, name, in old);

        Count(grown);
        return grown;
    }

    // A value the node was built with, which weighs nothing: LevelSeed's. A
    // clone comes into the level with what scripts set on its source.
    public void SetBuilt(SceneNode node, string name, in MemberValue value)
    {
        NodeAttributes own = OfOrMake(node);
        if (own.Set(name, in value, out MemberValue old) && _ofNode.ContainsKey(node))
            Count(-NodeWeight.OfSetAttribute(node, name, in old));
    }

    // A node whose last attribute went, and that has no signal, is kept
    // nothing for.
    public void Forget(SceneNode node, ReadOnlySpan<char> name)
    {
        bool isCounted = _ofNode.TryGetValue(node, out NodeAttributes? own);
        if (!isCounted)
            own = Of(node);

        if (own is null || !own.Remove(name, out string? held, out MemberValue old))
            return;

        if (isCounted)
            Count(-NodeWeight.OfSetAttribute(node, held ?? string.Empty, in old));

        if (own.Count == 0 && own.Changed is null && own.ChangedOf is null && !_ofNode.Remove(node))
            _ofDestroyed.Remove(node);
    }

    // Gives a copy what scripts set on its source, with the names a script
    // took away. The signals stay the source's own.
    public void Copy(SceneNode source, SceneNode copy)
    {
        if (Of(source) is not { Count: > 0 } set)
            return;

        foreach (KeyValuePair<string, MemberValue> pair in set)
            Set(copy, pair.Key, pair.Value);
    }

    // Takes everything scripts left on a node no script can reach any more,
    // and hands it over, for whoever ends its signals. Null when there is
    // nothing.
    public NodeAttributes? Take(SceneNode node)
    {
        if (!_ofNode.Remove(node, out NodeAttributes? own))
            return null;

        Count(-WeightOf(node, own));
        return own;
    }

    // The node was destroyed. It keeps what scripts set on it. All it had is
    // handed over, for whoever ends its signals. Null when there is nothing.
    public NodeAttributes? Destroyed(SceneNode node)
    {
        if (!_ofNode.Remove(node, out NodeAttributes? own))
            return null;

        Count(-WeightOf(node, own));
        if (own.Count > 0)
            _ofDestroyed.AddOrUpdate(node, own);

        return own;
    }

    // What scripts set on the node weighs, while it is not destroyed.
    public long BytesOf(SceneNode node) => _ofNode.TryGetValue(node, out NodeAttributes? own) ? WeightOf(node, own) : 0;

    // What Bytes should be, counted again from every node. For a test.
    public long CountBytes()
    {
        long bytes = 0;
        foreach (KeyValuePair<SceneNode, NodeAttributes> pair in _ofNode)
            bytes += WeightOf(pair.Key, pair.Value);

        return bytes;
    }

    public void Clear()
    {
        _ofNode.Clear();
        _ofDestroyed.Clear();
        _names.Clear();
        Count(-Bytes);
    }

    private static long WeightOf(SceneNode node, NodeAttributes own)
    {
        long bytes = 0;
        foreach (KeyValuePair<string, MemberValue> pair in own)
            bytes += NodeWeight.OfSetAttribute(node, pair.Key, pair.Value);

        return bytes;
    }

    private void Count(long bytes)
    {
        Bytes += bytes;
        _bytes.Add(bytes);
    }

    // A name stays when its last attribute is taken away. So each time there
    // are twice as many as were in use the last time, the names no node has
    // an attribute or a signal of are let go.
    private void LetUnusedNamesGo()
    {
        _names.Clear();
        foreach (NodeAttributes own in _ofNode.Values)
        {
            foreach (KeyValuePair<string, MemberValue> pair in own)
                _names.Add(pair.Key);

            if (own.ChangedOf is not { } signals)
                continue;

            foreach (string name in signals.Keys)
                _names.Add(name);
        }

        _lookOverAt = Math.Max(FirstLookOver, _names.Count * 2);
    }
}
