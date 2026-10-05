using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

// A scene for the sound tests: a recorder named "sink", a catalog that knows
// one second of sound, and a log of which output fired on which tick.
internal sealed class SoundRig
{
    public const int Rate = 48_000;

    public const string OneSecond = "Sounds/one_second.wav";

    public SoundRig()
    {
        Catalog.Add(OneSecond, new SoundDescription(Rate, Rate));
        EntityRuntime.Place(Scene.Root, "sink", "test_recorder");
    }

    public Scene Scene { get; } = new("Sounds");

    // What the sink received, as "sink:Input:parameter".
    public List<string> Heard { get; } = [];

    public CapturingLogger Logger { get; } = new();

    public FakeSoundCatalog Catalog { get; } = new();

    public WireFireLog Trace { get; } = new();

    public List<string> Fired => Trace.Fired;

    // A sound named "sound" with both outputs wired to the sink.
    public SceneNode Sound(string path = OneSecond, params (string Key, string Value)[] keys)
    {
        SceneNode node = SoundUnder(Scene.Root, "sound", path, keys);
        Movers.WireToSink(node, PointSound.OnEnded, PointSound.OnMarker);
        return node;
    }

    // A sound with no wires.
    public SceneNode SoundUnder(SceneNode parent, string name, string path, params (string Key, string Value)[] keys)
    {
        var data = new EntityData("point_sound");
        data.SetValue("sound", path);
        foreach ((string key, string value) in keys)
            data.SetValue(key, value);

        SceneNode node = parent.CreateChild(name);
        node.Entity = data;
        return node;
    }

    public EntityWorld Start()
    {
        var world = new EntityWorld(Scene, Logger, EntityRuntime.Catalog(Heard))
        {
            SoundCatalog = Catalog,
            Trace = Trace,
        };

        world.Activate();
        return world;
    }
}
