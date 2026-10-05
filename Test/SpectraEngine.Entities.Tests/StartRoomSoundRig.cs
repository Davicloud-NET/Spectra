using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Projects;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

// The demo's start room with its sounds cooked, played on a device with no
// sound card. Joined the way the engine joins them: a tick, then the
// listener, the device and the presenter.
internal sealed class StartRoomSoundRig : IDisposable
{
    public const float Dt = PhysicsDefaults.FixedDeltaTime;

    // With walls, sound is heard through them as the engine hears it.
    public StartRoomSoundRig(CookedDemoSounds cooked, bool walls = false)
    {
        // As the demo mounts them: the content root, and below it the folder
        // its sounds were cooked into.
        var content = new ContentSourceStack();
        content.Mount(new LooseFileSource(NullLogger.Instance, ContentRoot.Path));
        content.Mount(new LooseFileSource(NullLogger.Instance, cooked.Folder, priority: -1));
        Assets = new AssetManager(NullLogger.Instance, ContentRoot.Path, content, hotReloadEnabled: false);

        Manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = EntityRuntime.Catalog([]),
            EntityTrace = Outputs,
        };

        var renderer = new FakeRenderer();
        Manager.LoadStartupScene(renderer, Assets);
        Scene = Manager.ActiveScene.ShouldNotBeNull();

        // The course alone. The baseplate was only there to give the manager a scene.
        for (int i = Scene.Root.Children.Count - 1; i >= 0; i--)
            Scene.Root.RemoveChild(Scene.Root.Children[i]);

        DemoPlayArea.Build(Scene, MaterialRef.Default, MaterialRef.Default, MaterialRef.Default);
        Scene.RebuildStaticWorld(renderer);

        Audio = new AudioManager(NullLogger.Instance, Supply);
        Audio.Initialize();
        var captions = new CaptionFeed(new CaptionLibrary(Assets.Content, LanguageTag.Default, NullLogger.Instance));
        ISoundPropagation propagation = walls
            ? new WallPropagation(
                new SceneSoundObstacles(() => Manager.ActiveScene), Assets.Acoustics, settings: null, WallClock)
            : new DirectPropagation();
        Presenter = new SoundPresenter(Audio, Assets, propagation, captions, Log);

        Character = new CharacterSimulation(Scene) { FallOutHeight = DemoPlayArea.FallOutHeight };
        Session = new PlaySession(Manager, Character);
    }

    // What the presenter logged.
    public CapturingLogger Log { get; } = new();

    // What the walls read an answer's age from. It moves a tick a frame.
    public ManualClock WallClock { get; } = new();

    public FiredOutputLog Outputs { get; } = new();

    public AssetManager Assets { get; }

    public SceneManager Manager { get; }

    public Scene Scene { get; }

    public FakeAudioBackend Backend { get; } = new();

    public AudioManager Audio { get; }

    public SoundPresenter Presenter { get; }

    // Speech only until a test sets it, as the engine starts.
    public CaptionFeed Captions => Presenter.Captions;

    public CharacterSimulation Character { get; }

    public PlaySession Session { get; }

    public EntityWorld World => Manager.EntityWorld.ShouldNotBeNull();

    public void Dispose()
    {
        Audio.Shutdown();

        // Before the cooked folder is deleted, or the open sounds still hold their files.
        Assets.Shutdown();
    }

    // One tick, then what the engine does for sound each frame.
    public void Tick(in CharacterCommand command)
    {
        Session.Tick(Dt, in command);
        Present();
    }

    public void Idle(int ticks)
    {
        for (int i = 0; i < ticks; i++)
            Tick(default);
    }

    // Stands on the lift and uses the button, which is on the wall to its
    // south. The button takes the press on the tick after this.
    public void PressTheLiftButton()
    {
        // Onto the middle of the lift, whose top is 0.3 up.
        Character.Teleport(new Vector3(123f, 0.35f, -3f));
        Idle(30);

        var press = new CharacterCommand
        {
            Yaw = MathF.Atan2(1.5f, -0.85f),
            Buttons = CharacterButtons.Use,
        };
        Tick(in press);
    }

    // The listener is the player's eye, as the camera is in play.
    public void Present() =>
        PresentFrom(Character.State.Position + new Vector3(0f, Character.Tuning.EyeHeight, 0f));

    public void PresentFrom(Vector3 ear)
    {
        WallClock.Advance(Dt);
        Audio.SetListener(ear, Vector3.UnitX, Vector3.UnitY);
        Audio.Update();
        Presenter.Update(Manager.EntityWorld, Dt);
    }

    public SceneNode Node(string name) =>
        Find(Scene.Root, name) ?? throw new InvalidOperationException($"The start room has no node named '{name}'.");

    public T Live<T>(string name)
        where T : Entity => EntityRuntime.Live<T>(World, Node(name));

    // The playing sound on the node of that name, if it is playing.
    public bool TryGetEmitter(string name, out SoundEmitter emitter)
    {
        foreach (SoundEmitter playing in World.Sounds.Playing)
        {
            if (playing.Node.Name == name)
            {
                emitter = playing;
                return true;
            }
        }

        emitter = default;
        return false;
    }

    public bool IsPlaying(string name) => TryGetEmitter(name, out _);

    // What the device's playing sources at a world position were last told.
    public List<AudioSourceSettings> SourcesAt(Vector3 position)
    {
        var found = new List<AudioSourceSettings>();
        foreach (uint source in Backend.PlayingSources())
        {
            AudioSourceSettings settings = Backend.SettingsOf(source);
            if (!settings.Relative && settings.Position == position)
                found.Add(settings);
        }

        return found;
    }

    private static SceneNode? Find(SceneNode node, string name)
    {
        if (node.Name == name)
            return node;

        foreach (SceneNode child in node.Children)
        {
            if (Find(child, name) is { } found)
                return found;
        }

        return null;
    }

    private bool Supply(ILogger logger, [NotNullWhen(true)] out IAudioBackend? backend, out string reason)
    {
        backend = Backend;
        reason = string.Empty;
        return true;
    }
}
