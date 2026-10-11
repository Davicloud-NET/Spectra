namespace SpectraEngine.Core.Entities;

internal enum EntityEventKind : byte
{
    Think = 0,

    // Delivered to every entity the target name resolves to.
    Input = 1,
}
