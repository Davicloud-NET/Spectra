using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>
/// Runs the scripts of one level that is playing. It owns one Luau state,
/// which lives from the start of the level to its stop, and it is the only
/// thing in that state that knows of a scene. A host installs
/// <see cref="Create"/> on <see cref="ScriptHosting.RuntimeFactory"/>.
/// </summary>
public sealed class LuauScriptRuntime : IScriptRuntime
{
    private readonly EntityWorld _world;
    private readonly NodeNamingSink _sink;
    private readonly PrintCap _prints;
    private readonly LuauHost _host;
    private readonly WaitingThreads _waiting = new();
    private readonly TaskLibrary _tasks;
    private readonly RunService _runService;
    private readonly ScriptStarter _starter;
    private readonly ChildWaits _childWaits;
    private readonly NodeSignals _signals;
    private readonly ScriptCharacter _character;
    private readonly DeferredCalls _deferred;
    private readonly LevelTags _tags;
    private readonly AttributeMethods _attributes;
    private readonly NodeLifetime _lifetime;
    private readonly LevelSeed _seed;
    private readonly NodeBytes _nodeBytes;
    private volatile bool _isClosed;

    /// <summary>Opens a Luau state and gives it everything a script of this level can reach.</summary>
    /// <exception cref="InvalidOperationException">
    /// The native Luau library is missing or from another build. The message
    /// says which, and how to build it.
    /// </exception>
    public LuauScriptRuntime(ScriptRuntimeSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        _world = setup.World;
        _sink = new NodeNamingSink(setup.Sink);
        _prints = new PrintCap(_sink, _world);
        _host = new LuauHost(_prints);
        _childWaits = new ChildWaits(_host, _world, _waiting);

        // What scripts leave on nodes for each other: tags and attributes.
        var hub = new SignalHub(_host, _waiting);
        _deferred = new DeferredCalls(_world);
        _nodeBytes = new NodeBytes(_host);
        _tags = new LevelTags(_host, _world, hub, _deferred, new TagTable(_nodeBytes));
        _attributes = new AttributeMethods(hub, _deferred, new AttributeTable(_nodeBytes));
        _seed = new LevelSeed(_host, _world, _tags.Table, _attributes.Table);
        _tags.Seed = _seed;
        var tagMethods = new TagMethods(_tags);

        try
        {
            var context = new ScriptContext(_host, new ScriptWrites(_world), _childWaits, tagMethods, _attributes);
            InstallValues(context);

            _tasks = new TaskLibrary(_host, _world, _waiting);
            _tasks.ScriptInstall(context);
            new OldTaskGlobals(_tasks).ScriptInstall(context);

            _runService = new RunService(new SignalHub(_host, _waiting));
            SceneNode workspace = _world.Scene.Root;
            var players = new Players(new PlayerWrites(_world));
            _character = players.Body;
            var game = new DataModel(_host, workspace, _runService, players, new CollectionService(tagMethods));
            new LevelGlobals(game, workspace).ScriptInstall(context);
            var modules = new ModuleLoader(_host, setup.Source, _sink, _waiting);
            modules.ScriptInstall(context);

            // No library: an entity class is data, and its members are found by name.
            _signals = new NodeSignals(new SignalHub(_host, _waiting), _world);
            context.Entities = new EntityMembers(_host, _world, _signals);
            context.Signals = _signals;

            _starter = new ScriptStarter(_host, _world, setup.Source, _sink);
            _lifetime = new NodeLifetime(context, _starter, modules, _signals, _tags);
            context.Lifetime = _lifetime;
            new InstanceLibrary(_lifetime).ScriptInstall(context);
            _tags.ChangedOutside = _lifetime.Changed;
            _tags.HasRoom = _lifetime.HasRoom;
            _host.Objects.Reached = _lifetime.Reached;
            _host.Objects.Released = _lifetime.Released;

            _host.Seal();

            // Last, so a runtime that was not made listens to nothing.
            _childWaits.Listen();
            _tags.Listen();
        }
        catch
        {
            _host.Dispose();
            throw;
        }
    }

    // The types a script holds by value, and the objects.
    private static void InstallValues(ScriptContext context)
    {
        Vector3Members.Install(context);
        Vector3Library.ScriptInstall(context);

        // Before the library: a userdata gets its tag's metatable when it
        // is made, and the library makes CFrame.identity.
        CFrameMembers.Install(context);
        CFrameLibrary.ScriptInstall(context);
        ObjectMembers.Install(context);
        Color3Members.Install(context);
        Color3Library.ScriptInstall(context);
    }

    /// <summary>
    /// How long one call into a script may run before the script is stopped
    /// and switched off. See <see cref="LuauHost.CallLimit"/>.
    /// </summary>
    public TimeSpan CallLimit
    {
        get => _host.CallLimit;
        set => _host.CallLimit = value;
    }

    /// <summary>
    /// How many bytes the level's scripts may hold before the one that is
    /// running is stopped. See <see cref="LuauHost.MemoryBudget"/>.
    /// </summary>
    public long MemoryBudget
    {
        get => _host.MemoryBudget;
        set => _host.MemoryBudget = value;
    }

    /// <summary>How many threads wait, for a tick or for a signal.</summary>
    public int WaitingThreadCount => _waiting.Count;

    /// <summary>How many nodes have a tag, built with it or given it by a script. A destroyed node is not counted. None once the level has stopped.</summary>
    public int TaggedNodeCount => _tags.Table.NodeCount;

    /// <summary>How many nodes hold an attribute, built with it or set by a script, or a signal of one a script asked for. A destroyed node is not counted. None once the level has stopped.</summary>
    public int AttributeNodeCount => _attributes.Table.NodeCount;

    // How many tags are kept, with those nothing waits on until they are looked over.
    internal int KnownTagCount => _tags.Table.TagCount;

    // How many destroyed nodes still keep a tag, and how many an attribute.
    // Each walks them all. None once the level has stopped.
    internal int DestroyedTaggedCount => _tags.Table.DestroyedCount;

    internal int DestroyedAttributeCount => _attributes.Table.DestroyedCount;

    /// <summary>How many scripts run for a node of the level, with those their host switched off.</summary>
    public int RunningScriptCount => _starter.RunningCount;

    /// <summary>How many trees scripts made are in no level, and counted until they are found to be let go of.</summary>
    public int LooseTreeCount => _lifetime.LooseCount;

    /// <summary>How many signals of nodes scripts have asked for, such as a part's Touched.</summary>
    public int NodeSignalCount => _signals.SignalCount;

    /// <summary>How many nodes and other objects scripts hold, or held until Luau's collector says otherwise. One that only grows is a leak.</summary>
    public int HeldObjectCount => _host.IsClosed ? 0 : _host.Objects.Count;

    // How many destroyed nodes scripts made count on, as far as is known.
    internal int HeldDestroyedCount => _lifetime.HeldCount;

    // What those nodes keep, by the estimate Luau's collector is told, and
    // the most there was of each since the level started.
    internal long HeldDestroyedBytes => _host.IsClosed ? 0 : _lifetime.Held.Bytes;

    internal int PeakHeldDestroyedCount => _lifetime.Held.PeakCount;

    internal long PeakHeldDestroyedBytes => _lifetime.Held.PeakBytes;

    // How many cycles of Luau's collector the destroyed nodes stepped to
    // their end, and how many times one of them made it step.
    internal int HeldCollectionCount => _lifetime.Held.Cycles;

    internal int HeldStepCount => _lifetime.Held.Steps;

    // The held bytes from which a node that adds more makes the collector step.
    internal long HeldGoalBytes => _lifetime.Held.Goal;

    // What Luau counts for the scripts, garbage included.
    internal long LuauBytes => _host.MemoryUsed;

    // What the tags and attributes scripts set on nodes that are not
    // destroyed weigh, as the memory budget counts them, and the same counted
    // again from every node.
    internal long NodeBytes => _host.NodeBytes;

    internal long PeakNodeBytes => _nodeBytes.Peak;

    internal int PeakLooseTreeCount => _lifetime.PeakLooseCount;

    internal long CountNodeBytes() => _tags.Table.CountBytes() + _attributes.Table.CountBytes();

    // Has Luau collect in full, so the counts above say what is held now,
    // and so does the count of made nodes.
    internal void Collect()
    {
        if (_isClosed)
            return;

        _host.Collect();
        _lifetime.LetGoOfUnheld();
    }

    /// <summary>
    /// Whether the Luau state is gone, and every thread with it. After
    /// <see cref="Close"/> that is at once, or when the call into a script
    /// that was running has returned.
    /// </summary>
    public bool IsClosed => _host.IsClosed;

    /// <summary>Makes the runtime for a level. This is the factory a host installs.</summary>
    public static IScriptRuntime Create(ScriptRuntimeSetup setup) => new LuauScriptRuntime(setup);

    /// <inheritdoc/>
    public void StartScripts()
    {
        if (_isClosed)
            return;

        try
        {
            // First, so the first line of the first script finds the tags
            // and attributes the level was built with.
            _seed.ReadLevel();
            _starter.StartAll(_world.Scene);
        }
        catch (Exception e) // boundary: nothing of a script may throw into the tick
        {
            ReportFault(e);
        }
    }

    /// <inheritdoc/>
    public void Wake(long token)
    {
        if (_isClosed)
            return;

        try
        {
            if (!_deferred.TryRun(token) && !_childWaits.TryWake(token))
                _tasks.Wake(token);
        }
        catch (Exception e) // boundary: nothing of a script may throw into the tick
        {
            ReportFault(e);
        }
    }

    /// <inheritdoc/>
    public bool Drop(long token)
    {
        if (_isClosed)
            return true;

        try
        {
            // A listener's call is gone with its wake.
            if (_deferred.TryDrop(token))
                return true;

            // A thread that waits for a child waits on. Any other is ended.
            if (_childWaits.TryDrop(token))
                return false;

            _tasks.Drop(token);
        }
        catch (Exception e) // boundary: nothing of a script may throw into the tick
        {
            ReportFault(e);
        }

        return true;
    }

    /// <inheritdoc/>
    public void Hear(long listener, string parameter)
    {
        if (_isClosed)
            return;

        try
        {
            _signals.Hear(listener, parameter);
        }
        catch (Exception e) // boundary: nothing of a script may throw into the tick
        {
            ReportFault(e);
        }
    }

    /// <inheritdoc/>
    // The player's touch hands the listener the one character the level's
    // scripts have, so a script can hold it against the one it knows. Also
    // when the player has left since: an end has to say whose touch ended.
    public void HearTouch(long listener, in TouchVisitor visitor)
    {
        if (_isClosed)
            return;

        try
        {
            _signals.HearTouch(listener, visitor.Id == TouchVisitor.PlayerId ? _character : null);
        }
        catch (Exception e) // boundary: nothing of a script may throw into the tick
        {
            ReportFault(e);
        }
    }

    /// <inheritdoc/>
    public void BeforeSimulation()
    {
        if (_isClosed)
            return;

        try
        {
            _runService.FireBeforeSimulation(_world.Time, _world.FixedDeltaTime);
        }
        catch (Exception e) // boundary: nothing of a script may throw into the tick
        {
            ReportFault(e);
        }
    }

    /// <inheritdoc/>
    public void AfterSimulation()
    {
        if (_isClosed)
            return;

        try
        {
            _runService.FireAfterSimulation(_world.FixedDeltaTime);
            _childWaits.EndTick();

            // A view of the level shows who listens and who has sent. A
            // listener nothing can call any more is not one, and neither is a
            // script that was switched off. Only a watched level is looked over.
            if (_world.Trace is not null)
            {
                _signals.HideTheDead();
                _starter.NoteStopped(_world.StoppedScripts);
            }

            // What scripts let go of since the last tick.
            _lifetime.EndTick();
            _prints.EndTick();
        }
        catch (Exception e) // boundary: nothing of a script may throw into the tick
        {
            ReportFault(e);
        }
    }

    /// <inheritdoc/>
    public void StartEntered()
    {
        if (_isClosed)
            return;

        try
        {
            _starter.StartEntered();
        }
        catch (Exception e) // boundary: nothing of a script may throw into the tick
        {
            ReportFault(e);
        }
    }

    /// <inheritdoc/>
    // The host closes at once when no call into a script is running. When one
    // is, as after the thread that ran the level died inside it, the host
    // closes if that call ever returns. Nothing here waits or touches Luau,
    // and what the runtime keeps of the state is dropped with it.
    public void Close()
    {
        _isClosed = true;
        _childWaits.StopListening();
        _tags.StopListening();
        _host.Close();

        // Not under a call that has not returned, which may be reading them.
        if (_host.IsClosed)
        {
            _deferred.Clear();
            _tags.Table.Clear();
            _attributes.Table.Clear();
            _seed.Clear();
        }
    }

    private void ReportFault(Exception fault) =>
        _sink.Report(new ScriptMessage(
            ScriptSeverity.Error, $"The script runtime failed: {fault.Message}", string.Empty, 0));
}
