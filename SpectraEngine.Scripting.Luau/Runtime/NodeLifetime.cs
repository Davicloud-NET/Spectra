using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// What it means to the level's scripts that a node is made, cloned, moved
// in or out of the level, or destroyed. TreeWrites changes the tree. This
// does the rest: a script below a node that leaves ends and one below a node
// that comes in starts. Of a node that was destroyed only its tags and
// attributes are kept, and nothing of one no script can reach any more.
internal sealed class NodeLifetime
{
    // What a script reads when it changes the tree before the runtime is whole.
    public const string NotSetUp = "the level's tree cannot be changed before its scripts are set up";

    private readonly LuauHost _host;
    private readonly TreeWrites _tree;
    private readonly ScriptStarter _starter;
    private readonly ModuleLoader _modules;
    private readonly ChildWaits _waits;
    private readonly NodeSignals _signals;
    private readonly LevelTags _tags;
    private readonly AttributeMethods _attributes;
    private readonly LooseNodes _loose;
    private readonly HeldNodes _held;

    // Ending a thread can run a script, which can come back in here. So
    // each call takes a list of its own, and gives it back.
    private readonly Stack<List<SceneNode>> _spareLists = [];
    private readonly Stack<List<NodeCopy>> _spareCopies = [];

    public NodeLifetime(
        ScriptContext context, ScriptStarter starter, ModuleLoader modules, NodeSignals signals, LevelTags tags)
    {
        _host = context.Host;
        _tree = context.Tree;
        _starter = starter;
        _modules = modules;
        _waits = context.ChildWaits;
        _signals = signals;
        _tags = tags;
        _attributes = context.Attributes;
        _loose = new LooseNodes(_host, _tree, Forget);
        _held = new HeldNodes(_host, _tree);
    }

    // How many made trees are in no level and may still be reached.
    public int LooseCount => _loose.Count;

    // How many destroyed nodes scripts made count on, as far as is known.
    public int HeldCount => _held.Count;

    // Instance.new: a node of a class, in no tree. Null with the reason
    // when none was made.
    public SceneNode? Make(ReadOnlySpan<char> className, out string? refusal)
    {
        // A node scripts made and let go of counts until it is found. So
        // when there is no room, they are looked for and it is tried again.
        LookOver();
        if (!_tree.TryMake(className, out SceneNode? made, out refusal) && _tree.IsOutOfRoom && MakeRoom())
            _tree.TryMake(className, out made, out refusal);

        if (made is not null)
            _loose.Add(made);

        return made;
    }

    // Clone: a copy of the node and all below it, in no tree, with the tags
    // and attributes scripts left on each.
    public SceneNode? Clone(SceneNode source, out string? refusal)
    {
        LookOver();
        List<NodeCopy> copies = _spareCopies.Count > 0 ? _spareCopies.Pop() : [];
        if (!_tree.TryClone(source, copies, out SceneNode? copy, out refusal) && _tree.IsOutOfRoom && MakeRoom())
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
                Destroyed(destroyed[i]);

            // After the ending, which can run a script that lets go.
            for (int i = 0; i < destroyed.Count; i++)
            {
                if (_host.Objects.Holds(destroyed[i]))
                    _held.Add(destroyed[i]);
            }
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

    // A script came to hold an object none held. A made node that had
    // stopped counting counts again: one that was destroyed, or one of a
    // tree that was let go of and came back from the level's queue.
    public void Reached(object reached)
    {
        if (reached is not SceneNode node || !TreeWrites.IsMade(node))
            return;

        if (TreeWrites.IsDestroyed(node))
            _held.Add(node);
        else if (_tree.CountAgain(node) && !_tree.IsInLevel(node))
            _loose.Add(TopOf(node));
    }

    // Stops counting every made node no script holds any more, so the count
    // says what is held now. For a test, which need not wait for a look-over.
    public void LetGoOfUnheld()
    {
        _loose.Sweep();
        _held.LetGo();
    }

    private void LookOver()
    {
        _loose.LookOver();
        _held.LookOver();
    }

    // Both ask Luau which nodes scripts still hold, by a full collection.
    private bool MakeRoom()
    {
        bool isFound = _loose.Sweep();
        return _held.Sweep() || isFound;
    }

    private static SceneNode TopOf(SceneNode node)
    {
        SceneNode top = node;
        while (top.Parent is { } above)
            top = above;

        return top;
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

    // Lets go of all the runtime keeps for a node of a tree no script can
    // reach any more, its tags and attributes too.
    private void Forget(SceneNode node)
    {
        EndAllOf(node);
        _tags.Table.Forget(node);
        _attributes.Table.Take(node)?.EndSignals();
    }

    // A destroyed node keeps its tags and attributes, as in Roblox, for a
    // script that still holds it. The tables hold it weakly from here on,
    // so they are let go with the node. Nothing of it fires again.
    private void Destroyed(SceneNode node)
    {
        EndAllOf(node);
        _tags.Table.Destroyed(node);
        _attributes.Table.Destroyed(node)?.EndSignals();
    }

    // Ends what runs for a node that is gone for good: its script, the
    // module it is, the connections to what it fires and the waits on it.
    // What a script holds stays.
    private void EndAllOf(SceneNode node)
    {
        _loose.Remove(node);
        _starter.Left(node);
        _modules.Forget(node);
        _signals.Forget(node);
        _waits.Destroyed(node);
    }

    private List<SceneNode> Rent() => _spareLists.Count > 0 ? _spareLists.Pop() : [];

    private void Return(List<SceneNode> list)
    {
        list.Clear();
        _spareLists.Push(list);
    }
}
