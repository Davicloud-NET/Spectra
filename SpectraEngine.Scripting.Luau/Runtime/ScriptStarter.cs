using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Loads and starts a level's scripts: all that are there on the first tick,
// in the order of the scene tree, and later each that comes into the level
// with a clone or a new parent. One that cannot start says which node it is
// on, and the rest still run. A script runs while its node is in the level.
[ScriptGlobals]
internal sealed partial class ScriptStarter(LuauHost host, EntityWorld world, IScriptSource source, NodeNamingSink sink)
{
    // The script that runs for each node, or that the host switched off.
    private readonly Dictionary<SceneNode, LuauScript> _running = new(ReferenceEqualityComparer.Instance);

    // The nodes whose scripts start at the end of the tick, in the order
    // they came into the level.
    private readonly List<SceneNode> _entered = [];
    private readonly List<SceneNode> _starting = [];

    // A file is read once for a level, however many clones name it.
    private readonly Dictionary<string, byte[]> _texts = new(StringComparer.Ordinal);

    public int RunningCount => _running.Count;

    public void StartAll(Scene scene)
    {
        // First the list, then the scripts. One that runs may add to the
        // tree or take from it, and a walk of the tree would lose its place.
        _starting.Clear();
        foreach (SceneNode node in scene.Root.Traverse())
        {
            if (node.Script is { IsEnabled: true, IsModule: false })
                _starting.Add(node);
        }

        StartEach(scene);
    }

    // A node came into the level. Its script starts at the end of the tick,
    // once the entities that came with it run.
    public void Entered(SceneNode node)
    {
        if (node.Script is { IsEnabled: true, IsModule: false } && !_running.ContainsKey(node))
            _entered.Add(node);
    }

    // A node left the level, for a while or for good. Its script is switched
    // off: a thread of it that is running goes on to its next wait and no
    // further, and one that waits is ended.
    public void Left(SceneNode node)
    {
        if (!_running.Remove(node, out LuauScript? script))
            return;

        // A view of the level's wiring forgets what the script sent.
        if (world.Trace is not null)
            world.StoppedScripts.Add(node.Id);

        script.Owner = null;
        script.SwitchOff();
    }

    public void StartEntered()
    {
        if (_entered.Count == 0)
            return;

        // What these scripts bring into the level starts a tick later.
        _starting.Clear();
        _starting.AddRange(_entered);
        _entered.Clear();
        StartEach(world.Scene);
    }

    // Tells the level which of its scripts were switched off, for a view of
    // what scripts have sent. A module is not among them: its code counts
    // for the script that asked for it.
    public void NoteStopped(StoppedScripts stopped)
    {
        foreach ((SceneNode node, LuauScript script) in _running)
        {
            if (script.IsSwitchedOff)
                stopped.Add(node.Id);
        }
    }

    /// <summary>
    /// The node of the script that is running. Each script has its own. It is nil in a module
    /// that was asked for by path, which has no node.
    /// </summary>
    // By hand: it is a global of one script and not of the state, so it is
    // set when the script is loaded.
    [ScriptProperty(Name = "script", Type = "Instance", ByHand = true)]
    private static LuauValue ScriptOf(SceneNode node) => LuauValue.Object(ScriptTags.Object, node);

    private void StartEach(Scene scene)
    {
        for (int i = 0; i < _starting.Count; i++)
        {
            // A close asked for from another thread takes the state when the
            // script that was running comes back.
            if (host.IsClosed)
                break;

            // A script that ran before this one may have taken the node out
            // of the level, or its script off it.
            SceneNode node = _starting[i];
            if (scene.TryFindById(node.Id, out SceneNode? found) && ReferenceEquals(found, node)
                && node.Script is { IsEnabled: true, IsModule: false } script && !_running.ContainsKey(node))
            {
                Start(node, script);
            }
        }

        _starting.Clear();
    }

    private void Start(SceneNode node, Script script)
    {
        if (Read(script.Path, out string? problem) is not { } text)
        {
            Report(node, script, problem is null
                ? $"'{node.Name}' names a script file that is not in the level's content, so it did not start."
                : $"{problem} The script on '{node.Name}' did not start.");
            return;
        }

        sink.Loading = node;
        LuauScript? loaded = host.Load(script.Path, text);
        sink.Loading = null;

        // The host has reported a script that did not compile.
        if (loaded is null)
            return;

        // What the script hears and sends is drawn from this node.
        loaded.Owner = node;
        _running.Add(node, loaded);
        world.StoppedScripts.Remove(node.Id);

        if (loaded.SetGlobal("script"u8, ScriptOf(node)))
            loaded.Start();
        else
            Report(node, script, $"The script on '{node.Name}' could not be given its node, so it did not start.");
    }

    private byte[]? Read(string path, out string? problem)
    {
        problem = null;
        if (_texts.TryGetValue(path, out byte[]? known))
            return known;

        byte[]? text = source.Read(path, out problem);
        if (text is not null)
            _texts.Add(path, text);

        return text;
    }

    // A script that did not start stays wrong until the level is played again.
    private void Report(SceneNode node, Script script, string text) =>
        sink.Report(new ScriptMessage(ScriptSeverity.Error, text, script.Path, 0) { ProblemNode = node.Id });
}
