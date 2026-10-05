using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;

namespace SpectraEngine.Bsp.Tests;

// Plays a sound while spawning, if the world's catalog knows it.
internal sealed class SpawnSoundEntity : Entity
{
    public const string Path = "Sounds/spawn.wav";

    public bool SawCatalog { get; private set; }

    public int Emitter { get; private set; }

    protected internal override void OnSpawn()
    {
        SawCatalog = World.SoundCatalog is not null;

        if (World.SoundCatalog is { } catalog && catalog.TryDescribe(Path, out SoundDescription sound, out _))
            Emitter = World.Sounds.Play(Node, Path, in sound, new SoundEmitterSettings(1f, 1f, 2f, 30f, false));
    }
}
