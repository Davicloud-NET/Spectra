using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The tags of a level's nodes while it plays. They live here and on no
// node, so Stop leaves none behind. This fires the signals of
// CollectionService: when a node in the level's tree gets or loses a tag,
// and when a tagged node comes into the tree or leaves it.
internal sealed class LevelTags
{
    // With fewer tags than this none is looked over.
    private const int FirstLookOver = 64;

    private readonly LuauHost _host;
    private readonly EntityWorld _world;
    private readonly SignalHub _hub;
    private readonly DeferredCalls _calls;
    private readonly Func<ScriptSignal, bool> _isHeld;
    private int _lookOverAt = FirstLookOver;

    public LevelTags(LuauHost host, EntityWorld world, SignalHub hub, DeferredCalls calls, TagTable table)
    {
        Table = table;
        _host = host;
        _world = world;
        _hub = hub;
        _calls = calls;
        _isHeld = host.Objects.Holds;
    }

    // Who has what. Adding here fires nothing, which is how a level's own
    // tags are put in before its scripts start.
    public TagTable Table { get; }

    // What reads a node that comes into the level, or null.
    public LevelSeed? Seed { get; set; }

    // Is told of a destroyed node that got a tag or lost one.
    public Action<SceneNode>? DestroyedChanged { get; set; }

    // Says whether the scripts are under the memory budget after a tag.
    public Func<bool>? HasRoom { get; set; }

    public void Listen()
    {
        Scene scene = _world.Scene;
        scene.NodeAdded += OnEntered;
        scene.NodeRemoved += OnLeft;
        scene.NodeReparented += OnMoved;
    }

    // Any thread may call this, as any may close the runtime.
    public void StopListening()
    {
        Scene scene = _world.Scene;
        scene.NodeAdded -= OnEntered;
        scene.NodeRemoved -= OnLeft;
        scene.NodeReparented -= OnMoved;
    }

    // A tag the node has already is added to nothing and fires nothing.
    // False when the tag took the scripts past the memory budget: it is not
    // kept then, and fires nothing either.
    public bool Add(SceneNode node, ReadOnlySpan<char> tag)
    {
        if (Table.TagCount >= _lookOverAt && Table.Find(tag) is null)
            LookOver();

        bool isInLevel = NodeTree.IsIn(_world.Scene.Root, node);
        long before = Table.Bytes;
        if (Table.Add(node, tag, isInLevel) is not { } added)
            return true;

        if (Table.Bytes > before && HasRoom?.Invoke() == false)
        {
            _host.StopForMemory();
            Table.Remove(node, tag, out _);
            return false;
        }

        if (isInLevel)
            Fire(added.Added, node);
        else if (TreeWrites.IsDestroyed(node))
            DestroyedChanged?.Invoke(node);

        return true;
    }

    // What AddTag returns when Add said false: the script is switched off.
    public int FailForMemory(LuauStack stack) => _host.FailForMemory(stack);

    // Nor does taking away one it lacks.
    public void Remove(SceneNode node, ReadOnlySpan<char> tag)
    {
        if (Table.Remove(node, tag, out bool wasInLevel) is not { } removed)
            return;

        if (wasInLevel)
            Fire(removed.Removed, node);
        else if (TreeWrites.IsDestroyed(node))
            DestroyedChanged?.Invoke(node);
    }

    // The nodes of the level's tree with that tag, in the tree's order.
    public IReadOnlyList<SceneNode> Tagged(ReadOnlySpan<char> tag) =>
        Table.Find(tag) is { } found ? Table.InOrder(found) : [];

    // One signal for each tag, so a script reads the same object every time.
    public ScriptSignal AddedSignal(ReadOnlySpan<char> tag) =>
        Named(tag).Added ??= new ScriptSignal("Signal InstanceAdded", _hub);

    public ScriptSignal RemovedSignal(ReadOnlySpan<char> tag) =>
        Named(tag).Removed ??= new ScriptSignal("Signal InstanceRemoved", _hub);

    // The three below are scene events, raised in the middle of whatever
    // changed the level. So they change nothing and run no script: a
    // listener's call is queued.
    private void OnEntered(SceneNode node)
    {
        try
        {
            // A node that was never in the level brings what it was built with.
            Seed?.Read(node, isInLevel: false);
            if (Table.NodeCount == 0)
                return;

            IReadOnlyList<TagNodes> tags = Table.Enter(node);
            for (int i = 0; i < tags.Count; i++)
                Fire(tags[i].Added, node);
        }
        catch (Exception e) // boundary: whoever changed the level must not get a script's failure
        {
            Report(e);
        }
    }

    private void OnLeft(SceneNode node)
    {
        if (Table.NodeCount == 0)
            return;

        try
        {
            IReadOnlyList<TagNodes> tags = Table.Leave(node);
            for (int i = 0; i < tags.Count; i++)
                Fire(tags[i].Removed, node);
        }
        catch (Exception e) // boundary: whoever changed the level must not get a script's failure
        {
            Report(e);
        }
    }

    private void OnMoved(SceneNode node) => Table.TreeMoved();

    private TagNodes Named(ReadOnlySpan<char> tag)
    {
        if (Table.Find(tag) is { } kept)
            return kept;

        if (Table.TagCount >= _lookOverAt)
            LookOver();

        return Table.FindOrMake(tag);
    }

    // A tag no node has stays, so one that comes and goes is made once. Each
    // time there are twice as many tags as were left the last time, those
    // nothing waits on are let go. Which signals scripts still hold is for
    // Luau to say, and a full collection makes it say so now.
    private void LookOver()
    {
        if (Table.DropUnused() > 0)
        {
            _host.Collect();
            Table.DropUnheard(_isHeld);
        }

        _lookOverAt = Math.Max(FirstLookOver, Table.TagCount * 2);
    }

    private void Fire(ScriptSignal? signal, SceneNode node)
    {
        if (signal is { ListenerCount: > 0 })
            _calls.Fire(signal, LuauValue.Object(ScriptTags.Object, node));
    }

    private void Report(Exception fault) =>
        _host.Report(new ScriptMessage(
            ScriptSeverity.Error, $"The script runtime failed: {fault.Message}", string.Empty, 0));
}
