using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

/// <summary>
/// One loaded script: its code, a thread for its top level and globals of its
/// own. What it sets as a global, no other script sees.
/// </summary>
public sealed class LuauScript
{
    private readonly LuauHost _host;
    private readonly byte[] _chunkName;
    private readonly HashSet<LuauFunction> _functions = [];

    // The chunk name is "@" and the script's name, with a zero at the end.
    internal LuauScript(LuauHost host, string name, byte[] chunkName, int id, int threadReference)
    {
        _host = host;
        _chunkName = chunkName;
        Name = name;
        Id = id;
        ThreadReference = threadReference;
    }

    /// <summary>The script's name, a content path. Errors and prints carry it.</summary>
    public string Name { get; }

    /// <summary>
    /// Whether the script is switched off. The host does that to a script that ran too
    /// long or held too much memory, and <see cref="SwitchOff"/> does it for a script
    /// that left the level.
    /// </summary>
    public bool IsSwitchedOff { get; private set; }

    /// <summary>The thread its top level runs on.</summary>
    public LuauThread Thread { get; internal set; }

    internal int Id { get; }

    // Keeps the top-level thread, and through it the script's globals, for
    // the threads its kept functions are started on.
    internal int ThreadReference { get; private set; }

    internal ReadOnlySpan<byte> NameUtf8 => _chunkName.AsSpan(1, _chunkName.Length - 2);

    /// <summary>Runs the script's top level, up to its end or its first yield.</summary>
    public LuauResume Start(ReadOnlySpan<LuauValue> arguments = default) => Thread.Resume(arguments);

    /// <summary>
    /// Sets a global that only this script sees, such as <c>script</c>. Before
    /// <see cref="Start"/> only. False when it could not be set.
    /// </summary>
    public bool SetGlobal(ReadOnlySpan<byte> name, in LuauValue value)
    {
        if (_host.IsClosed || !Thread.IsAlive)
            return false;

        // The thread has not started, so its stack holds the top-level
        // function and takes a value above it.
        LuauStack stack = Thread.Values;
        if (value.PushTo(_host, stack) && stack.SetGlobal(name))
            return true;

        stack.Pop();
        return false;
    }

    /// <summary>
    /// Switches the script off for good and lets go of its threads, its kept functions and
    /// its globals. A thread of it that is running now goes on to its next yield.
    /// </summary>
    public void SwitchOff()
    {
        if (IsSwitchedOff)
            return;

        IsSwitchedOff = true;
        if (_host.IsClosed)
            return;

        _host.Threads.EndAllOf(this);

        // Dispose takes each one out of the set.
        foreach (LuauFunction function in _functions.ToArray())
            function.Dispose();

        // A top-level thread that is running could not be ended, and this
        // reference is all that keeps it. Its slot takes it until it waits.
        int reference = ThreadReference;
        ThreadReference = Lua.NilReference;
        if (!_host.Threads.TryHandOver(Thread, reference))
            Lua.lua_unref(_host.Stack.State, reference);
    }

    internal void Track(LuauFunction function) => _functions.Add(function);

    internal void Forget(LuauFunction function) => _functions.Remove(function);
}
