using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

/// <summary>
/// One Luau state and everything that runs in it. Make it, give it the host's
/// functions and tables through <see cref="Stack"/>, seal it, then load scripts.
/// One thread at a time may use it. Closing it ends every script.
/// </summary>
public sealed unsafe class LuauHost : IDisposable
{
    /// <summary>How long one call into Luau may run before its script is stopped.</summary>
    public static readonly TimeSpan DefaultCallLimit = TimeSpan.FromSeconds(3);

    /// <summary>How many bytes the scripts may hold before the running one is stopped.</summary>
    public const long DefaultMemoryBudget = 256 * Megabyte;

    private const long Megabyte = 1024 * 1024;

    private readonly IScriptSink _sink;
    private readonly nint _main;
    private readonly CallWatchdog _watchdog;
    private readonly List<LuauScript> _scripts = [];
    private readonly Lock _closing = new();
    private GCHandle _handle;
    private long _memoryBudget = DefaultMemoryBudget;
    private volatile bool _isClosed;
    private volatile bool _isCloseWanted;

    /// <summary>Opens a state with Luau's libraries, and with <c>print</c> and <c>warn</c> going to the sink.</summary>
    public LuauHost(IScriptSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (LuauLibrary.FindProblem() is { } problem)
            throw new InvalidOperationException(problem);

        _sink = sink;
        _main = Shim.sl_newstate();
        if (_main == 0)
            throw new InvalidOperationException("Luau made no state.");

        _handle = GCHandle.Alloc(this);
        Shim.sl_sethost(_main, GCHandle.ToIntPtr(_handle));
        Shim.sl_setbudget(_main, _memoryBudget);

        Functions = new HostFunctions();
        Threads = new ThreadRunner(this, _main);
        Objects = new LuauObjects(this);
        _watchdog = new CallWatchdog(_main, DefaultCallLimit);

        if (Shim.sl_openlibs(_main) != LuaStatus.Ok || !ScriptOutput.Install(_main))
        {
            string reason = LuaText.Read(_main, -1) ?? "no reason given";
            Close();
            throw new InvalidOperationException($"Luau's libraries did not open: {reason}");
        }
    }

    /// <summary>
    /// The host's own stack. Before <see cref="Seal"/> it is where the functions and tables
    /// every script sees are built and set as globals.
    /// </summary>
    public LuauStack Stack => _isClosed ? throw new ObjectDisposedException(nameof(LuauHost)) : new LuauStack(_main);

    /// <summary>The managed objects scripts hold.</summary>
    public LuauObjects Objects { get; }

    /// <summary>Whether the shared globals are read-only and scripts can be loaded.</summary>
    public bool IsSealed { get; private set; }

    /// <summary>Whether the state is gone.</summary>
    public bool IsClosed => _isClosed;

    /// <summary>
    /// How long one call into Luau may run. A script that passes it stops with an error at
    /// the line it was on and is switched off. Zero or less turns the limit off.
    /// </summary>
    public TimeSpan CallLimit
    {
        get => _watchdog.Limit;
        set => _watchdog.Limit = value;
    }

    /// <summary>
    /// How many bytes the state may hold. When it holds more after a full collection, the
    /// script that is running stops with an error and is switched off. Zero or less turns
    /// the budget off.
    /// </summary>
    public long MemoryBudget
    {
        get => _memoryBudget;
        set
        {
            _memoryBudget = value;
            if (!_isClosed)
                Shim.sl_setbudget(_main, value);
        }
    }

    /// <summary>How many bytes the state holds now, garbage included.</summary>
    public long MemoryUsed => _isClosed ? 0 : Shim.sl_memory(_main);

    internal HostFunctions Functions { get; }

    internal ThreadRunner Threads { get; }

    /// <summary>The host whose state a host function was called with. Null when there is none.</summary>
    public static LuauHost? From(LuauStack stack) => FromState(stack.State);

    /// <summary>
    /// Pushes a function that calls the delegate, for everything that need not be fast.
    /// An exception it throws reaches the script as an error. The delegate is kept until
    /// the host closes.
    /// </summary>
    public bool PushFunction(LuauStack stack, ReadOnlySpan<byte> name, LuauHostFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return !_isClosed && Functions.Push(stack.State, name, function);
    }

    /// <summary>
    /// Gives each name a number, its place in the list. A host function then switches on
    /// <see cref="LuauStack.TryGetAtom"/> and <see cref="LuauStack.NamecallAtom"/> where it would
    /// compare names. Once, before the host is sealed.
    /// </summary>
    /// <param name="names">The names as UTF-8, each ended by a zero byte.</param>
    /// <param name="count">How many names there are.</param>
    /// <exception cref="InvalidOperationException">The list was not taken, or a list is already set.</exception>
    public void SetAtoms(ReadOnlySpan<byte> names, int count)
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (IsSealed)
            throw new InvalidOperationException("Set the atoms before the host is sealed.");

        fixed (byte* text = names)
        {
            if (Shim.sl_setatoms(_main, text, (nuint)names.Length, count) == 0)
            {
                throw new InvalidOperationException(
                    "The atoms were not taken: the host has a list already, or a name is in it twice, " +
                    "or the names do not each end with a zero.");
            }
        }
    }

    /// <summary>
    /// Makes every global read-only, with each table it holds, so no script can change what all
    /// scripts share. Scripts can be loaded from here on.
    /// </summary>
    public void Seal()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (IsSealed)
            return;

        if (Shim.sl_sandbox(_main) != LuaStatus.Ok)
        {
            string reason = LuaText.Read(_main, -1) ?? "no reason given";
            Shim.sl_pop(_main, 1);
            throw new InvalidOperationException($"The Luau state could not be sealed: {reason}");
        }

        IsSealed = true;
        _watchdog.Start();
    }

    /// <summary>
    /// Compiles a script and gives it a thread and globals of its own. A script with a
    /// mistake in it goes to the sink as an error with its line, and null comes back.
    /// </summary>
    /// <param name="name">The script's name, a content path. Errors and prints carry it.</param>
    /// <param name="source">The script's text as UTF-8.</param>
    public LuauScript? Load(string name, ReadOnlySpan<byte> source)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ObjectDisposedException.ThrowIf(_isClosed, this);
        if (!IsSealed)
            throw new InvalidOperationException("Seal the host before a script is loaded.");

        nint thread;
        int reference;
        if (Shim.sl_spawn(_main, 0, 1, &thread, &reference) != LuaStatus.Ok)
        {
            Report(new ScriptMessage(ScriptSeverity.Error, TakeError(_main, "the script got no thread"), name, 0));
            return null;
        }

        // Windows editors put a byte order mark in front, which Luau takes for a mistake.
        if (source.StartsWith(Encoding.UTF8.Preamble))
            source = source[Encoding.UTF8.Preamble.Length..];

        byte[] chunkName = ChunkName(name);
        if (LuaChunk.Load(thread, chunkName, source) != LuaStatus.Ok)
        {
            string text = TakeError(thread, "the script did not load");
            Lua.lua_unref(_main, reference);
            Report(ScriptReport.CompileError(name, text));
            return null;
        }

        LuauScript script = new(this, name, chunkName, _scripts.Count, reference);
        _scripts.Add(script);
        script.Thread = Threads.Add(thread, reference: 0, script);
        return script;
    }

    /// <summary>The script whose code called a host function. Null when no script's did.</summary>
    public LuauScript? ScriptOf(LuauStack stack) => _isClosed ? null : ScriptOf(stack.State);

    /// <summary>
    /// The thread a host function runs on, for a function that yields and wants to be the
    /// one to resume it. A coroutine a script made is kept from here on.
    /// </summary>
    public LuauThread ThreadOf(LuauStack stack) => _isClosed ? default : Threads.Of(stack.State);

    /// <summary>
    /// The thread at a stack index, for a host function that is handed a coroutine. One
    /// the host did not start is kept from here on. When there is no thread there, the
    /// handle that comes back refuses everything.
    /// </summary>
    public LuauThread ThreadAt(LuauStack stack, int index)
    {
        if (_isClosed || stack.TypeOf(index) != LuauType.Thread)
            return default;

        // TypeOf has checked the index.
        return Threads.Of(Lua.lua_tothread(stack.State, index));
    }

    /// <summary>
    /// Runs a function on a thread of its own, for the calling script. The function sits
    /// under its arguments, as the top <paramref name="count"/> values of the stack. All
    /// of them are taken.
    /// </summary>
    public LuauResume Start(LuauStack source, int count, out LuauThread thread)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return Threads.Start(source.State, count, ScriptOf(source.State), out thread);
    }

    /// <summary>
    /// Makes the thread as <see cref="Start"/> does and does not run it, for a function
    /// that is to run later. Its first resume starts it. False when no thread could be
    /// made, as for a script that is switched off.
    /// </summary>
    public bool Prepare(LuauStack source, int count, out LuauThread thread) =>
        Threads.Prepare(source.State, count, ScriptOf(source.State), out thread);

    /// <summary>Keeps the function at the index for the calling script. Null when there is none there.</summary>
    public LuauFunction? KeepFunction(LuauStack stack, int index) =>
        !_isClosed && stack.TypeOf(index) == LuauType.Function && TryKeep(stack, index, out int reference)
            ? new LuauFunction(this, reference, ScriptOf(stack.State))
            : null;

    /// <summary>Keeps the value at the index alive. Null when it could not be kept.</summary>
    public LuauReference? Keep(LuauStack stack, int index) =>
        TryKeep(stack, index, out int reference) ? new LuauReference(this, reference) : null;

    /// <summary>Pushes a value. False: it could not be pushed, and an error value is on top instead.</summary>
    public bool Push(LuauStack stack, in LuauValue value) => !_isClosed && value.PushTo(this, stack);

    /// <summary>Runs a full collection and lets go of the objects Luau no longer holds.</summary>
    public void Collect()
    {
        if (_isClosed)
            return;

        Lua.lua_gc(_main, LuaGc.Collect, 0);
        Objects.ReleaseCollected();
    }

    /// <summary>
    /// Closes the state, with every script and thread in it. Any thread may close a host
    /// that no thread is using. From inside a call into Luau, the close waits for that
    /// call to end.
    /// </summary>
    public void Close()
    {
        lock (_closing)
        {
            _isCloseWanted = true;
            CloseWhenIdle();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Close();

    internal static LuauHost? FromState(nint L) => FromHandle(Shim.sl_host(L));

    internal static LuauHost? FromHandle(nint handle) =>
        handle == 0 ? null : GCHandle.FromIntPtr(handle).Target as LuauHost;

    internal LuauScript? ScriptOf(nint state)
    {
        int id = ThreadData.ScriptOf(state);
        return (uint)id < (uint)_scripts.Count ? _scripts[id] : null;
    }

    internal void Report(in ScriptMessage message) => _sink.Report(in message);

    // Pushes them all, or none: false leaves the stack as it was. A value
    // that cannot be pushed is the host's own mistake, such as a tag with no
    // metatable, so the sink hears of it.
    internal bool PushAll(LuauStack stack, ReadOnlySpan<LuauValue> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].PushTo(this, stack))
                continue;

            string reason = stack.GetText(-1) ?? "no reason given";
            stack.Pop(i + 1);
            Report(new ScriptMessage(ScriptSeverity.Error, $"the host could not hand a value to a script: {reason}", string.Empty, 0));
            return false;
        }

        return true;
    }

    // What the sink is told in place of Luau's own message when the host stopped a script.
    internal string Explain(int stop)
    {
        string budget = _memoryBudget >= Megabyte ? $"{_memoryBudget / Megabyte} MB" : $"{_memoryBudget / 1024} KB";

        return stop == Shim.StopMemory
            ? $"the scripts hold more than {budget} and this one was switched off"
            : string.Create(CultureInfo.InvariantCulture, $"the script ran for more than {CallLimit.TotalSeconds:0.###} s without a pause and was switched off");
    }

    // For whatever just returned from a call into Luau. Nothing may touch the
    // state after this.
    internal void AfterCall()
    {
        if (!_isCloseWanted)
            return;

        lock (_closing)
            CloseWhenIdle();
    }

    private void CloseWhenIdle()
    {
        if (_isClosed || (Shim.sl_callstate(_main) & 1) != 0)
            return;

        _isClosed = true;
        _watchdog.Stop();
        Shim.sl_close(_main);
        _handle.Free();
    }

    private bool TryKeep(LuauStack stack, int index, out int reference)
    {
        reference = Lua.NilReference;
        if (_isClosed)
            return false;

        int kept;
        if (Shim.sl_keep(stack.State, index, &kept) != LuaStatus.Ok)
        {
            stack.Pop();
            return false;
        }

        reference = kept;
        return true;
    }

    private static string TakeError(nint L, string fallback)
    {
        string text = LuaText.Read(L, -1) ?? fallback;
        Shim.sl_pop(L, 1);
        return text;
    }

    // "@" tells Luau the name is a file's, and a zero ends it.
    private static byte[] ChunkName(string name)
    {
        byte[] chunkName = new byte[Encoding.UTF8.GetByteCount(name) + 2];
        chunkName[0] = (byte)'@';
        Encoding.UTF8.GetBytes(name, chunkName.AsSpan(1));
        return chunkName;
    }
}
