using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using System.Numerics;
using System.Text;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The four yes or no settings of the sound entity that say what is worked
/// out for it: placed, fades, walls and doppler.
/// </summary>
public sealed class PointSoundSimulationTests
{
    // A sound with all four switched off, and one that says nothing.
    private const string SoundFixture = """
        {
          "spectramap": 4,
          "minimumReadableVersion": 3,
          "engine": "1.0.0",
          "scene": {
            "name": "SoundFixture"
          },
          "nodes": [
            {
              "id": "3b1f6c0e-52a7-4e19-9d3c-8a4e7f2b6c11",
              "name": "Alarm",
              "transform": {"p":[4,2,-6]},
              "entity": {
                "class": "point_sound",
                "keys": {"doppler":"0","fades":"0","placed":"0","sound":"Sounds/one_second.wav","startplaying":"1","walls":"0"}
              },
              "children": []
            },
            {
              "id": "9e27d4a8-6c35-4b0f-a1d2-5f8c3e7b9a42",
              "name": "Hum",
              "transform": {"p":[0,1,0]},
              "entity": {
                "class": "point_sound",
                "keys": {"looped":"1","sound":"Sounds/one_second.wav","startplaying":"1"}
              },
              "children": []
            }
          ]
        }
        """;

    private readonly SoundRig _rig = new();

    [Fact]
    public void A_sound_with_nothing_set_has_all_four_on()
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        sound.IsPlaced.ShouldBeTrue();
        sound.FadesWithDistance.ShouldBeTrue();
        sound.IsMuffledByWalls.ShouldBeTrue();
        sound.HasDoppler.ShouldBeTrue();
        world.Sounds.Playing[0].Simulated.ShouldBe(SoundSimulation.All);
    }

    [Theory]
    [InlineData("placed", SoundSimulation.Placed)]
    [InlineData("fades", SoundSimulation.Fades)]
    [InlineData("walls", SoundSimulation.Walls)]
    [InlineData("doppler", SoundSimulation.Doppler)]
    public void A_setting_at_0_switches_its_part_off_on_the_emitter_and_no_other(string key, SoundSimulation part)
    {
        _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"), (key, "0"));
        EntityWorld world = _rig.Start();

        world.Sounds.Playing[0].Simulated.ShouldBe(SoundSimulation.All & ~part);
        _rig.Logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty();
    }

    [Fact]
    public void All_four_at_0_leave_nothing_worked_out_and_all_four_at_1_leave_everything()
    {
        _rig.SoundUnder(
            _rig.Scene.Root, "bare", SoundRig.OneSecond,
            ("startplaying", "1"), ("placed", "0"), ("fades", "0"), ("walls", "0"), ("doppler", "0"));
        _rig.SoundUnder(
            _rig.Scene.Root, "full", SoundRig.OneSecond,
            ("startplaying", "1"), ("placed", "1"), ("fades", "1"), ("walls", "1"), ("doppler", "1"));

        EntityWorld world = _rig.Start();

        world.Sounds.Playing[0].Simulated.ShouldBe(SoundSimulation.None);
        world.Sounds.Playing[1].Simulated.ShouldBe(SoundSimulation.All);
    }

    [Fact]
    public void The_settings_hold_for_every_later_play()
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("fades", "0"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        EntityRuntime.Send(sound, "Play");
        EntityRuntime.Send(sound, "Play");

        world.Sounds.Playing.Length.ShouldBe(1);
        world.Sounds.Playing[0].Simulated.ShouldBe(SoundSimulation.All & ~SoundSimulation.Fades);
    }

    [Theory]
    [InlineData("placed")]
    [InlineData("fades")]
    [InlineData("walls")]
    [InlineData("doppler")]
    public void A_setting_that_is_not_0_or_1_is_refused_and_the_part_stays_on(string key)
    {
        _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"), (key, "off"));
        EntityWorld world = _rig.Start();

        _rig.Logger.MessagesAt(LogLevel.Warning).ShouldBe(
        [
            $"Entity 'sound' (point_sound) cannot read keyvalue '{key}' = 'off'; keeping the default.",
        ]);
        world.Sounds.Playing[0].Simulated.ShouldBe(SoundSimulation.All);
    }

    [Fact]
    public void The_schema_lists_the_four_as_yes_or_no_settings_that_start_at_yes()
    {
        KeyvalueDescriptor[] switches = [.. PointSound.SpectraSchema.Keyvalues.TakeLast(4)];

        switches.Select(keyvalue => keyvalue.Name).ShouldBe(["placed", "fades", "walls", "doppler"]);
        switches.Select(keyvalue => keyvalue.Display).ShouldBe(
            ["Placed", "Fades with distance", "Muffled by walls", "Doppler"]);
        switches.ShouldAllBe(keyvalue => keyvalue.Type == KeyvalueType.Bool);
        switches.ShouldAllBe(keyvalue => keyvalue.Default == "1");
        switches.ShouldAllBe(keyvalue => keyvalue.Tooltip.Length > 0);
    }

    [Fact]
    public void An_editor_reads_the_four_from_the_schema_file()
    {
        EntitySchemaCatalog catalog = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(BuiltinEntities.Schemas));

        catalog.TryGetSchema("point_sound", out EntitySchema? found).ShouldBeTrue();
        KeyvalueDescriptor fades = found.ShouldNotBeNull().Keyvalues.Single(keyvalue => keyvalue.Name == "fades");

        fades.Type.ShouldBe(KeyvalueType.Bool);
        fades.Display.ShouldBe("Fades with distance");
        fades.Default.ShouldBe("1");
    }

    [Fact]
    public void The_four_settings_survive_a_level_saved_and_loaded()
    {
        byte[] source = Encoding.UTF8.GetBytes(SoundFixture.ReplaceLineEndings("\n") + "\n");
        var scene = new Scene("Empty");
        MapSceneBinder.ApplyTo(MapReader.Read(source), scene);

        byte[] saved = MapWriter.Write(MapSceneBinder.FromScene(scene));
        Encoding.UTF8.GetString(saved).ShouldBe(Encoding.UTF8.GetString(source));

        var again = new Scene("Empty");
        MapSceneBinder.ApplyTo(MapReader.Read(saved), again);
        var world = new EntityWorld(again, _rig.Logger, EntityRuntime.Catalog([])) { SoundCatalog = _rig.Catalog };
        world.Activate();

        SoundEmitter alarm = world.Sounds.Playing.ToArray().Single(emitter => emitter.Node.Name == "Alarm");
        SoundEmitter hum = world.Sounds.Playing.ToArray().Single(emitter => emitter.Node.Name == "Hum");
        alarm.Simulated.ShouldBe(SoundSimulation.None);
        hum.Simulated.ShouldBe(SoundSimulation.All);
        _rig.Logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty();
    }

    [Fact]
    public void Doppler_at_its_strongest_moves_no_tick_of_a_sound_that_plays_once()
    {
        (List<string> fired, float highest) bent = PlayWhileTheListenerRunsAtIt(dopplerStrength: 4f);
        (List<string> fired, float highest) plain = PlayWhileTheListenerRunsAtIt(dopplerStrength: 0f);

        // Heard well over a semitone high, and the level counted the same ticks.
        bent.highest.ShouldBeGreaterThan(1.25f);
        plain.highest.ShouldBe(1f);
        bent.fired.ShouldBe(["30:OnMarker:half", "60:OnEnded"]);
        plain.fired.ShouldBe(bent.fired);
    }

    // A second of sound, started with the level 60 units ahead of a listener
    // who runs at it at 20 units a second. Returns what fired on which tick
    // and the highest pitch the device was given.
    private static (List<string> Fired, float Highest) PlayWhileTheListenerRunsAtIt(float dopplerStrength)
    {
        using var rig = new PointSoundDeviceRig();
        rig.Presenter.Simulation.DopplerStrength = dopplerStrength;
        rig.Sound(new Vector3(0, 0, -60), ("startplaying", "1"), ("maxdistance", "1000"));
        EntityWorld world = rig.Start();

        float highest = 0f;
        for (int tick = 1; tick <= 90; tick++)
        {
            rig.Step(world, new Vector3(0, 0, -20f * tick * PointSoundDeviceRig.Dt));

            foreach (float pitch in rig.Pitches())
                highest = MathF.Max(highest, pitch);
        }

        return (rig.Trace.Fired, highest);
    }
}
