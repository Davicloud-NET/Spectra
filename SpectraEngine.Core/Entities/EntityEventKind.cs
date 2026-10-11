namespace SpectraEngine.Core.Entities;

internal enum EntityEventKind : byte
{
    Think = 0,

    // Delivered to every entity the target name resolves to.
    Input = 1,

    // Handed back to the script runtime, which knows what its token names.
    ScriptWake = 2,
}
