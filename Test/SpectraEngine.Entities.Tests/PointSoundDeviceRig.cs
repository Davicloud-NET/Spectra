using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Audio;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Captions;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Projects;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

// A level with the built-in classes, one cooked sound, a presenter and an
// audio device with no sound card: a sound entity and what is heard of it.
internal sealed class PointSoundDeviceRig : IDisposable
{
    public const float Dt = 1f / 60f;

    // One second, mono, with a marker named "half" in the middle.
    public const string Tone = "Sounds/tone.wav";

    private const int Rate = 48_000;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "SpectraPointSoundDeviceTests", Guid.NewGuid().ToString("N"));

    private readonly AssetManager _assets;

    public PointSoundDeviceRig()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Sounds"));
        File.WriteAllBytes(
            Path.Combine(_root, "Sounds", "tone.saudio"),
            SaudioWriter.Write(
                new AudioFormat(Rate, 1),
                new short[Rate],
                LoopRegion.None,
                positional: true,
                markers: [new AudioMarker(Rate / 2, "half")]));

        _assets = new AssetManager(NullLogger.Instance, _root, hotReloadEnabled: false);
        Audio = new AudioManager(NullLogger.Instance, Supply);
        Audio.Initialize();

        var captions = new CaptionFeed(new CaptionLibrary(_assets.Content, LanguageTag.Default, NullLogger.Instance));
        Presenter = new SoundPresenter(Audio, _assets, new DirectPropagation(), captions, NullLogger.Instance);

        EntityRuntime.Place(Scene.Root, "sink", "test_recorder");
    }

    public Scene Scene { get; } = new("Sounds");

    public FakeAudioBackend Backend { get; } = new();

    public AudioManager Audio { get; }

    public SoundPresenter Presenter { get; }

    public WireFireLog Trace { get; } = new();

    public void Dispose()
    {
        Audio.Shutdown();

        // Before the delete, or the open sound still holds its file.
        _assets.Shutdown();
        Directory.Delete(_root, recursive: true);
    }

    // A sound named "sound" that plays the tone, with both outputs wired to the sink.
    public SceneNode Sound(Vector3 position, params (string Key, string Value)[] keys)
    {
        var data = new EntityData("point_sound");
        data.SetValue("sound", Tone);
        foreach ((string key, string value) in keys)
            data.SetValue(key, value);

        SceneNode node = Scene.Root.CreateChild("sound");
        node.LocalPosition = position;
        node.Entity = data;
        Movers.WireToSink(node, PointSound.OnEnded, PointSound.OnMarker);
        return node;
    }

    public EntityWorld Start()
    {
        var world = new EntityWorld(Scene, new CapturingLogger(), EntityRuntime.Catalog([]))
        {
            SoundCatalog = new AssetSoundCatalog(_assets),
            Trace = Trace,
        };

        world.Activate();
        return world;
    }

    // One tick, then what the engine does for sound each frame, heard from ear.
    public void Step(EntityWorld world, Vector3 ear)
    {
        world.Tick(Dt);
        Audio.SetListener(ear, -Vector3.UnitZ, Vector3.UnitY);
        Audio.Update();
        Presenter.Update(world, Dt);
    }

    // The pitch of every voice on the device now.
    public IEnumerable<float> Pitches()
    {
        foreach (uint source in Backend.PlayingSources())
            yield return Backend.SettingsOf(source).Pitch;
    }

    private bool Supply(ILogger logger, [NotNullWhen(true)] out IAudioBackend? backend, out string reason)
    {
        backend = Backend;
        reason = string.Empty;
        return true;
    }
}
