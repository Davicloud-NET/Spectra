namespace SpectraEngine.Scripting.Luau.Runtime;

// One thread that waits for a tick.
internal readonly record struct WaitingThread(
    LuauThread Thread,

    // The tick the wait began on, for the time that passed.
    long SinceTick,

    WakeReply Reply,

    // Values a script handed over for a thread that already existed, kept in
    // a table until the thread goes on. Null when there are none.
    LuauReference? Arguments,

    int ArgumentCount);
