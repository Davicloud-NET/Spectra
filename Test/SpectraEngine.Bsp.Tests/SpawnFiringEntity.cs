using SpectraEngine.Core.Entities;

namespace SpectraEngine.Bsp.Tests;

// Fires an output while spawning, before the first tick.
internal sealed class SpawnFiringEntity : Entity
{
    public const string OnSpawned = nameof(OnSpawned);

    protected internal override void OnSpawn() => FireOutput(OnSpawned);
}
