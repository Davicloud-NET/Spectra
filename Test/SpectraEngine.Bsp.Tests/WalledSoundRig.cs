using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// The presenter's rig with walls: its propagation traces the rig's own scene,
// which a span level builds into, on a clock the rig moves a frame at a time.
internal sealed class WalledSoundRig : IDisposable
{
    // With material files, a wall is made of what its .spectramat in the
    // rig's content says, as in the engine. Without, of what the span level's
    // materials are named for.
    public WalledSoundRig(int sources = AudioManager.DefaultSourceCount, bool materialFiles = false)
    {
        Scene? scene = null;
        var files = new AssetAcoustics();
        IAcousticMaterials materials = materialFiles ? files : WallRig.SpanMaterials();

        Walls = new WallPropagation(new SceneSoundObstacles(() => scene), materials, settings: null, Clock);
        Sound = new SoundPresenterRig(sources, Walls);

        files.Assets = Sound.Assets;
        scene = Sound.Scene;
        Level = new SpanLevel(scene: scene);
    }

    public ManualClock Clock { get; } = new();

    public WallPropagation Walls { get; }

    public SoundPresenterRig Sound { get; }

    // Builds into the scene the sounds are in.
    public SpanLevel Level { get; }

    public void Dispose() => Sound.Dispose();

    // A tick of the level and a frame of sound each, sixty to the second.
    public void Step(int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            Clock.Advance(SoundPresenterRig.TickSeconds);
            Sound.Tick();
            Sound.Frame();
        }
    }

    // The asset manager's table, which exists only once the rig does.
    private sealed class AssetAcoustics : IAcousticMaterials
    {
        public AssetManager? Assets { get; set; }

        public AcousticPreset Resolve(MaterialRef material) =>
            Assets?.Acoustics.Resolve(material) ?? AcousticPresets.Generic;
    }
}
