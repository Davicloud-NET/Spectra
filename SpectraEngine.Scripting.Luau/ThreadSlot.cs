namespace SpectraEngine.Scripting.Luau;

// One thread the host started or took in. A LuauThread names its slot and
// the generation the slot had then, so a handle to a thread that is gone
// finds nothing, also once the slot holds another thread.
internal struct ThreadSlot
{
    // Zero while the slot is free.
    internal nint State;

    // The registry reference that keeps the thread, or 0 when its script does.
    internal int Reference;

    internal uint Generation;
    internal LuauScript? Script;

    // How many values it yielded last.
    internal int ValueCount;

    internal int NextFree;
}
