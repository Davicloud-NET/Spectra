using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;

namespace SpectraEngine.Bsp.Tests;

// Plays a sound as it is removed, the way a breakable would.
internal sealed class RemoveSoundEntity : Entity
{
    public int Emitter { get; private set; }

    protected internal override void OnRemove() =>
        Emitter = World.Sounds.Play(
            Node, "Sounds/break.wav", new SoundDescription(48_000, 48_000), new SoundEmitterSettings(1f, 1f, 2f, 30f, false));
}
