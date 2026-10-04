using SpectraEngine.Core.Entities;

namespace SpectraEngine.Entities.Tests;

// Fires an output while spawning, which no built-in class does.
internal sealed class SpawnFiringEntity : Entity
{
    public const string OnSpawned = nameof(OnSpawned);

    protected override void OnSpawn() => FireOutput(OnSpawned);
}
