using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

// A level, a device with no sound card and a presenter, joined the way the
// engine joins them. The sounds are cooked files in a folder of its own.
internal sealed class SoundPresenterRig : IDisposable
{
    public const float TickSeconds = 1f / 60f;
    public const int Rate = 48_000;

    // One second, mono.
    public const string Beep = "Sounds/beep.wav";

    // Two seconds, mono, marked as too long for one buffer.
    public const string Speech = "Sounds/speech.wav";

    // One second, stereo.
    public const string Music = "Sounds/music.wav";

    public static readonly SoundEmitterSettings Once = new(1f, 1f, 2f, 30f, IsLooped: false);
    public static readonly SoundEmitterSettings Looped = new(1f, 1f, 2f, 30f, IsLooped: true);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SpectraSoundPresenterTests", Guid.NewGuid().ToString("N"));

    private readonly AssetSoundCatalog _catalog;

    public SoundPresenterRig(int sources = AudioManager.DefaultSourceCount, ISoundPropagation? propagation = null)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Sounds"));
        Cook(Beep, HandBuiltSaudio.Resident(frames: Rate));
        Cook(Speech, HandBuiltSaudio.Streaming(frames: 2 * Rate, framesPerEntry: 4096));
        Cook(Music, HandBuiltSaudio.Resident(frames: Rate, channels: 2));

        Assets = new AssetManager(NullLogger.Instance, _root, hotReloadEnabled: false);
        _catalog = new AssetSoundCatalog(Assets);

        Backend = new FakeAudioBackend(sources);
        Audio = new AudioManager(new CapturingLogger(), Supply, sources);
        Audio.Initialize();

        Presenter = new SoundPresenter(Audio, Assets, propagation ?? new DirectPropagation(), Log);
        World = StartLevel();
    }

    // What the presenter logged. Nothing else writes here.
    public CapturingLogger Log { get; } = new();

    public AssetManager Assets { get; }

    public FakeAudioBackend Backend { get; }

    public AudioManager Audio { get; }

    public SoundPresenter Presenter { get; }

    public Scene Scene { get; } = new("Sounds");

    public EntityWorld World { get; private set; }

    public SoundStats Stats => Presenter.Stats;

    public void Dispose()
    {
        Audio.Shutdown();

        // Before the delete, or the open sounds still hold their files.
        Assets.Shutdown();
        Directory.Delete(_root, recursive: true);
    }

    // Writes cooked bytes where the authored path's cooked file belongs.
    public void Cook(string authoredPath, byte[] saudio) =>
        File.WriteAllBytes(
            Path.Combine(_root, Path.ChangeExtension(authoredPath, ".saudio").Replace('/', Path.DirectorySeparatorChar)),
            saudio);

    // Another world over the same scene, as the next play session has.
    public EntityWorld StartLevel()
    {
        World = new EntityWorld(Scene, new CapturingLogger(), new EntityCatalog());
        World.Activate();
        return World;
    }

    public SceneNode Place(string name, Vector3 position)
    {
        SceneNode node = Scene.Root.CreateChild(name);
        node.LocalPosition = position;
        return node;
    }

    // Plays a cooked sound the way an entity does: described by the catalog.
    public int Play(SceneNode node, string path, SoundEmitterSettings settings)
    {
        _catalog.TryDescribe(path, out SoundDescription sound, out string reason).ShouldBeTrue(reason);
        return World.Sounds.Play(node, path, in sound, in settings);
    }

    public void Listen(Vector3 position) => Audio.SetListener(position, -Vector3.UnitZ, Vector3.UnitY);

    public void Tick(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
            World.Tick(TickSeconds);
    }

    // What the engine does for sound each frame, on the level the rig holds.
    public void Frame(int frames = 1)
    {
        for (int i = 0; i < frames; i++)
        {
            Audio.Update();
            Presenter.Update(World, TickSeconds);
        }
    }

    // What the one playing source was last told.
    public AudioSourceSettings OnlyVoice() => Backend.SettingsOf(Backend.PlayingSources().ShouldHaveSingleItem());

    // Whether a source is playing at a world position.
    public bool HasVoiceAt(Vector3 position)
    {
        foreach (uint source in Backend.PlayingSources())
        {
            AudioSourceSettings settings = Backend.SettingsOf(source);
            if (!settings.Relative && settings.Position == position)
                return true;
        }

        return false;
    }

    private bool Supply(ILogger logger, [NotNullWhen(true)] out IAudioBackend? backend, out string reason)
    {
        backend = Backend;
        reason = string.Empty;
        return true;
    }
}
