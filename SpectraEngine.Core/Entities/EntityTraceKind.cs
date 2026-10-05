namespace SpectraEngine.Core.Entities;

/// <summary>What an <see cref="EntityTraceEvent"/> reports.</summary>
public enum EntityTraceKind : byte
{
    /// <summary>An output fired, whether or not anything is wired to it.</summary>
    OutputFired = 0,

    /// <summary>An input was put on the queue, by a wire or by a host.</summary>
    InputQueued = 1,

    /// <summary>An input reached an entity. Recorded before the entity handles it.</summary>
    InputDelivered = 2,

    /// <summary>The entity an input just reached has no input of that name.</summary>
    InputRefused = 3,

    /// <summary>An input came due and there was nothing to send it to.</summary>
    TargetMissing = 4,
}
