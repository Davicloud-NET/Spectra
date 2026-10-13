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
    private readonly LuauHost _host;
    private readonly EntityWorld _world;
    private readonly SignalHub _hub;
    private readonly DeferredCalls _calls;

    public LevelTags(LuauHost host, EntityWorld world, SignalHub hub, DeferredCalls calls)
    {
        _host = host;
        _world = world;
        _hub = hub;
        _calls = calls;
    }

    // Who has what. Adding here fires nothing, which is how a level's own
    // tags would be put in before its scripts start.
    public TagTable Table { get; } = new();

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
    public void Add(SceneNode node, ReadOnlySpan<char> tag)
    {
        bool isInLevel = NodeTree.IsIn(_world.Scene.Root, node);
        if (Table.Add(node, tag, isInLevel) is { } added && isInLevel)
            Fire(added.Added, node);
    }

    // Nor does taking away one it lacks.
    public void Remove(SceneNode node, ReadOnlySpan<char> tag)
    {
        if (Table.Remove(node, tag, out bool wasInLevel) is { } removed && wasInLevel)
            Fire(removed.Removed, node);
    }

    // The nodes of the level's tree with that tag, in the tree's order.
    public IReadOnlyList<SceneNode> Tagged(ReadOnlySpan<char> tag) =>
        Table.Find(tag) is { } found ? Table.InOrder(found) : [];

    // One signal for each tag, so a script reads the same object every time.
    public ScriptSignal AddedSignal(ReadOnlySpan<char> tag)
    {
        TagNodes named = Table.FindOrMake(tag);
        return named.Added ??= new ScriptSignal("Signal InstanceAdded", _hub);
    }

    public ScriptSignal RemovedSignal(ReadOnlySpan<char> tag)
    {
        TagNodes named = Table.FindOrMake(tag);
        return named.Removed ??= new ScriptSignal("Signal InstanceRemoved", _hub);
    }

    // The three below are scene events, raised in the middle of whatever
    // changed the level. So they change nothing and run no script: a
    // listener's call is queued.
    private void OnEntered(SceneNode node)
    {
        if (Table.NodeCount == 0)
            return;

        try
        {
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

    private void Fire(ScriptSignal? signal, SceneNode node)
    {
        if (signal is { ListenerCount: > 0 })
            _calls.Fire(signal, LuauValue.Object(ScriptTags.Object, node));
    }

    private void Report(Exception fault) =>
        _host.Report(new ScriptMessage(
            ScriptSeverity.Error, $"The script runtime failed: {fault.Message}", string.Empty, 0));
}
