using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What it means to the level's scripts that a node is made, cloned, moved
// in or out of the level, or destroyed. TreeWrites changes the tree. This
// does the rest: a script below a node that leaves ends and one below a node
// that comes in starts, and nothing is kept for a node that is gone for good.
internal sealed class NodeLifetime
{
    // What a script reads when it changes the tree before the runtime is whole.
    public const string NotSetUp = "the level's tree cannot be changed before its scripts are set up";

    private readonly TreeWrites _tree;
    private readonly ScriptStarter _starter;
    private readonly ChildWaits _waits;
    private readonly NodeSignals _signals;
    private readonly LevelTags _tags;
    private readonly AttributeMethods _attributes;
    private readonly LooseNodes _loose;

    // Ending a thread can run a script, which can come back in here. So
    // each call takes a list of its own, and gives it back.
    private readonly Stack<List<SceneNode>> _spareLists = [];
    private readonly Stack<List<NodeCopy>> _spareCopies = [];

    public NodeLifetime(
        LuauHost host, TreeWrites tree, ScriptStarter starter, ChildWaits waits, NodeSignals signals, LevelTags tags,
        AttributeMethods attributes)
    {
        _tree = tree;
        _starter = starter;
        _waits = waits;
        _signals = signals;
        _tags = tags;
        _attributes = attributes;
        _loose = new LooseNodes(host, tree, Forget);
    }

    // What a script reads when it puts a tag or an attribute on a node that
    // was destroyed. The runtime keeps nothing for such a node.
    public static string HoldsNo(SceneNode node, string what) =>
        $"\"{node.Name}\" was destroyed, and a destroyed node holds no {what}";

    // How many made trees are in no level and may still be reached.
    public int LooseCount => _loose.Count;

    // Instance.new: a node of a class, in no tree. Null with the reason
    // when none was made.
    public SceneNode? Make(ReadOnlySpan<char> className, out string? refusal)
    {
        // A node scripts made and let go of counts until it is found. So
        // when there is no room, they are looked for and it is tried again.
        _loose.LookOver();
        if (!_tree.TryMake(className, out SceneNode? made, out refusal) && _tree.IsOutOfRoom && _loose.Sweep())
            _tree.TryMake(className, out made, out refusal);

        if (made is not null)
            _loose.Add(made);

        return made;
    }

    // Clone: a copy of the node and all below it, in no tree, with the tags
    // and attributes scripts left on each.
    public SceneNode? Clone(SceneNode source, out string? refusal)
    {
        _loose.LookOver();
        List<NodeCopy> copies = _spareCopies.Count > 0 ? _spareCopies.Pop() : [];
        if (!_tree.TryClone(source, copies, out SceneNode? copy, out refusal) && _tree.IsOutOfRoom && _loose.Sweep())
            _tree.TryClone(source, copies, out copy, out refusal);

        for (int i = 0; i < copies.Count; i++)
        {
            _tags.Table.Copy(copies[i].Source, copies[i].Copy);
            _attributes.Table.Copy(copies[i].Source, copies[i].Copy);
        }

        copies.Clear();
        _spareCopies.Push(copies);
        if (copy is not null)
            _loose.Add(copy);

        return copy;
    }

    // Destroy. Null, or why nothing was destroyed.
    public string? Destroy(SceneNode node)
    {
        List<SceneNode> destroyed = Rent();
        if (_tree.TryDestroy(node, destroyed, out string? refusal))
        {
            for (int i = 0; i < destroyed.Count; i++)
                Forget(destroyed[i]);
        }

        Return(destroyed);
        return refusal;
    }

    // The node got another parent, or none. `wasInLevel` is from before.
    public void Moved(SceneNode node, bool wasInLevel)
    {
        bool isInLevel = _tree.IsInLevel(node);
        if (TreeWrites.IsMade(node))
            _loose.Note(node);

        if (isInLevel != wasInLevel)
            Tell(node, isInLevel);

        // The scene tells of a child that arrives in the level. One that
        // arrives under a node in no level is told of here.
        if (!isInLevel && node.Parent is not null)
            _waits.Arrived(node);
    }

    // Tells the starter of every script at or below a node that came into
    // the level or left it.
    private void Tell(SceneNode top, bool cameIn)
    {
        List<SceneNode> nodes = Rent();
        nodes.Add(top);
        for (int i = 0; i < nodes.Count; i++)
        {
            IReadOnlyList<SceneNode> children = nodes[i].Children;
            for (int c = 0; c < children.Count; c++)
                nodes.Add(children[c]);
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            if (cameIn)
                _starter.Entered(nodes[i]);
            else
                _starter.Left(nodes[i]);
        }

        Return(nodes);
    }

    // Lets go of all the runtime keeps for a node that is gone for good:
    // its script, the connections to what it fires, the waits on it, its
    // tags and its attributes. What a script still holds of it stays.
    private void Forget(SceneNode node)
    {
        _loose.Remove(node);
        _starter.Left(node);
        _signals.Forget(node);
        _waits.Destroyed(node);
        _tags.Table.Forget(node);
        if (_attributes.Table.Take(node) is not { } left)
            return;

        left.Changed?.EndAll();
        if (left.ChangedOf is { } byName)
        {
            foreach (ScriptSignal signal in byName.Values)
                signal.EndAll();
        }
    }

    private List<SceneNode> Rent() => _spareLists.Count > 0 ? _spareLists.Pop() : [];

    private void Return(List<SceneNode> list)
    {
        list.Clear();
        _spareLists.Push(list);
    }
}
