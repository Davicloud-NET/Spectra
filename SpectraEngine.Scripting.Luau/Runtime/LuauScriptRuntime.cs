using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scripting;

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
    private readonly LuauHost _host;
    private readonly WaitingThreads _waiting = new();
    private readonly TaskLibrary _tasks;
    private readonly RunService _runService;
    private readonly ScriptStarter _starter;
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
        _host = new LuauHost(_sink);

        try
        {
            Vector3Library.Install(_host);

            // Before the library: a userdata gets its tag's metatable when it
            // is made, and the library makes CFrame.identity.
            CFrameMembers.Install(_host);
            CFrameLibrary.Install(_host);
            ObjectMembers.Install(_host, new NodeMembers(_host, _world));

            _tasks = new TaskLibrary(_host, _world, _waiting);
            _runService = new RunService(_host, new SignalMethods(_host, _waiting));
            SetGlobalObject("game"u8, new DataModel(_host, _world.Scene.Root, _runService));
            SetGlobalObject("workspace"u8, _world.Scene.Root);

            _host.Seal();
            _starter = new ScriptStarter(_host, setup.Source, _sink);
        }
        catch
        {
            _host.Dispose();
            throw;
        }
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

    /// <summary>How many threads wait, for a tick or for a signal.</summary>
    public int WaitingThreadCount => _waiting.Count;

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
            _tasks.Wake(token);
        }
        catch (Exception e) // boundary: nothing of a script may throw into the tick
        {
            ReportFault(e);
        }
    }

    /// <inheritdoc/>
    public void Drop(long token)
    {
        if (_isClosed)
            return;

        try
        {
            _tasks.Drop(token);
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

            // What scripts let go of since the last tick.
            _host.Objects.ReleaseCollected();
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
        _host.Close();
    }

    private void SetGlobalObject(ReadOnlySpan<byte> name, object value)
    {
        LuauStack stack = _host.Stack;
        if (!_host.Objects.Push(stack, ScriptTags.Object, value) || !stack.SetGlobal(name))
            throw new InvalidOperationException($"The script API could not be set up: {stack.GetText(-1)}");
    }

    private void ReportFault(Exception fault) =>
        _sink.Report(new ScriptMessage(
            ScriptSeverity.Error, $"The script runtime failed: {fault.Message}", string.Empty, 0));
}
