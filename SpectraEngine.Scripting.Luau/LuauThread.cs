namespace SpectraEngine.Scripting.Luau;

/// <summary>
/// A handle to a Luau thread the host keeps. It is a value: it costs nothing to copy,
/// and once the thread has ended every copy of it answers as gone.
/// </summary>
public readonly struct LuauThread : IEquatable<LuauThread>
{
    internal LuauThread(LuauHost host, int slot, uint generation)
    {
        Host = host;
        Slot = slot;
        Generation = generation;
    }

    internal LuauHost? Host { get; }

    internal int Slot { get; }

    internal uint Generation { get; }

    /// <summary>Whether the thread is still there: waiting, running or not yet started.</summary>
    public bool IsAlive => Host is { IsClosed: false } host && host.Threads.TryFind(this, out _);

    /// <summary>The script the thread belongs to, or null when it is gone or has none.</summary>
    public LuauScript? Script => Host?.Threads.ScriptOf(this);

    /// <summary>
    /// What the thread yielded, while it waits. For the thread that ended last, what it
    /// returned. Throws once neither holds. Read it before any thread runs again.
    /// </summary>
    public LuauStack Values => Find().Threads.ValuesOf(this, out _);

    /// <summary>How many values <see cref="Values"/> holds.</summary>
    public int ValueCount
    {
        get
        {
            Find().Threads.ValuesOf(this, out int count);
            return count;
        }
    }

    /// <summary>Starts the thread or goes on with it. The values are what its yield returns.</summary>
    public LuauResume Resume(ReadOnlySpan<LuauValue> values = default)
    {
        if (Host is not { IsClosed: false } host || !host.PushAll(host.Stack, values))
            return LuauResume.Refused;

        return host.Threads.Resume(this, host.Stack.State, values.Length);
    }

    /// <summary>
    /// The same with values that are already on a stack: the top <paramref name="count"/>
    /// of <paramref name="source"/>, which are taken.
    /// </summary>
    public LuauResume Resume(LuauStack source, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return Host is { } host ? host.Threads.Resume(this, source.State, count) : LuauResume.Refused;
    }

    /// <summary>
    /// Pushes the thread as a value a script can hold, or nil when it is gone. False: it
    /// could not be pushed, and an error value is on top instead.
    /// </summary>
    public bool Push(LuauStack stack) =>
        Host is null ? stack.PushNil() : !Host.IsClosed && Host.Threads.Push(this, stack.State);

    /// <summary>Ends a thread that waits, as <c>coroutine.close</c> does. False when it is running or gone.</summary>
    public bool Cancel() => Host is { } host && host.Threads.Cancel(this);

    /// <summary>Lets go of the thread without ending it. Luau frees it once no script holds it.</summary>
    public void Release() => Host?.Threads.Release(this);

    /// <inheritdoc/>
    public bool Equals(LuauThread other) =>
        ReferenceEquals(Host, other.Host) && Slot == other.Slot && Generation == other.Generation;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is LuauThread other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Slot, Generation);

    /// <summary>Whether two handles name the same thread.</summary>
    public static bool operator ==(LuauThread left, LuauThread right) => left.Equals(right);

    /// <summary>Whether two handles name different threads.</summary>
    public static bool operator !=(LuauThread left, LuauThread right) => !left.Equals(right);

    private LuauHost Find() =>
        Host ?? throw new InvalidOperationException("This is no thread: the handle was never set.");
}
