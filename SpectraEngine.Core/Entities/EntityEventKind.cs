namespace SpectraEngine.Core.Entities;

internal enum EntityEventKind : byte
{
    Think = 0,

    // Delivered to every entity the target name resolves to.
    Input = 1,

    // Handed back to the script runtime, which knows what its token names.
    ScriptWake = 2,

    // A fired output on its way to one listener of a script. The runtime
    // knows the listener by its token.
    ScriptSignal = 3,

    // A touch that started or ended, on its way to one listener of a script.
    ScriptTouch = 4,

    // A call of one listener of a signal a script set off. Handed back to
    // the script runtime as a wake is. No thread waits on it.
    ScriptCall = 5,
}
