using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// What the sound entity says about itself, in one line and in rows, while it
/// is stopped, playing and looping.
/// </summary>
public sealed class PointSoundStateTests
{
    private const string TwoSeconds = "Sounds/two_seconds.wav";

    private readonly SoundRig _rig = new();
    private readonly EntityHeadline _headline = new();

    public PointSoundStateTests() =>
        _rig.Catalog.Add(TwoSeconds, new SoundDescription(SoundRig.Rate * 2, SoundRig.Rate));

    [Fact]
    public void A_stopped_sound_says_stopped()
    {
        SceneNode node = _rig.Sound();
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        Headline(sound).ShouldBe(("state", "stopped"));
        Rows(sound).ShouldBe(
        [
            Row("playing", "0"),
            Row("seconds in", "0"),
            Row("seconds long", "1"),
            Row("times round", "0"),
            Row("volume", "1"),
            Row("pitch", "1"),
            Row("refused inputs", "0"),
        ]);
    }

    [Fact]
    public void A_playing_sound_says_how_far_in_it_is()
    {
        SceneNode node = _rig.Sound(TwoSeconds, ("startplaying", "1"), ("volume", "0.5"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        Headline(sound).ShouldBe(("playing", "0.0 of 2.0 s"));

        Movers.Run(world, 72);

        Headline(sound).ShouldBe(("playing", "1.2 of 2.0 s"));
        Rows(sound).ShouldBe(
        [
            Row("playing", "1"),
            Row("seconds in", "1.2"),
            Row("seconds long", "2"),
            Row("times round", "0"),
            Row("volume", "0.5"),
            Row("pitch", "1"),
            Row("refused inputs", "0"),
        ]);
    }

    [Fact]
    public void A_sound_that_ended_says_stopped_again()
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        Movers.Run(world, 60);

        Headline(sound).ShouldBe(("state", "stopped"));
        Rows(sound)[0].ShouldBe(Row("playing", "0"));
    }

    [Fact]
    public void A_looping_sound_says_where_in_the_pass_it_is_and_how_often_it_has_turned_round()
    {
        SceneNode node = _rig.Sound(TwoSeconds, ("startplaying", "1"), ("looped", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        Movers.Run(world, 150);

        Headline(sound).ShouldBe(("looping", "0.5 of 2.0 s"));
        Rows(sound)[..4].ShouldBe(
        [
            Row("playing", "1"),
            Row("seconds in", "0.5"),
            Row("seconds long", "2"),
            Row("times round", "1"),
        ]);
    }

    [Fact]
    public void A_looping_sound_with_a_region_measures_itself_against_the_end_of_the_region()
    {
        _rig.Catalog.Add(
            "Sounds/hum.wav",
            new SoundDescription(SoundRig.Rate * 2, SoundRig.Rate, new LoopRegion(24_000, 72_000)));
        SceneNode node = _rig.Sound("Sounds/hum.wav", ("startplaying", "1"), ("looped", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        // A second and a half is the first turn. A quarter second later it is
        // three quarters of a second into the file.
        Movers.Run(world, 105);

        Headline(sound).ShouldBe(("looping", "0.8 of 1.5 s"));
        Rows(sound)[1].ShouldBe(Row("seconds in", "0.75"));
    }

    [Fact]
    public void A_sound_that_cannot_play_says_it_has_no_sound()
    {
        SceneNode node = _rig.Sound("Sounds/gone.wav");
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        EntityRuntime.Send(sound, "Play");

        Headline(sound).ShouldBe(("state", "no sound"));
        Rows(sound)[..3].ShouldBe([Row("playing", "0"), Row("seconds in", "0"), Row("seconds long", "0")]);
    }

    [Fact]
    public void A_refused_input_shows_in_the_rows()
    {
        SceneNode node = _rig.Sound();
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);

        EntityRuntime.Send(sound, "SetVolume", "loud");
        EntityRuntime.Send(sound, "SetPitch", "0");
        EntityRuntime.Send(sound, "SetPitch", "1.5");

        Rows(sound)[^2..].ShouldBe([Row("pitch", "1.5"), Row("refused inputs", "2")]);
    }

    [Fact]
    public void Describing_a_playing_sound_changes_nothing()
    {
        SceneNode node = _rig.Sound(SoundRig.OneSecond, ("startplaying", "1"));
        EntityWorld world = _rig.Start();
        PointSound sound = EntityRuntime.Live<PointSound>(world, node);
        Movers.Run(world, 20);
        long version = world.Sounds.Version;

        List<KeyValuePair<string, string>> first = Rows(sound);
        List<KeyValuePair<string, string>> second = Rows(sound);

        second.ShouldBe(first);
        world.Sounds.Version.ShouldBe(version);

        Movers.Run(world, 100);
        _rig.Fired.ShouldBe(["60:OnEnded"]);
    }

    private (string Label, string Value) Headline(Entity entity)
    {
        _headline.Clear();
        entity.DescribeState(new EntityStateWriter(_headline));
        _headline.IsSet.ShouldBeTrue();
        return (_headline.Label, _headline.Value);
    }

    private static List<KeyValuePair<string, string>> Rows(Entity entity)
    {
        var rows = new List<KeyValuePair<string, string>>();
        entity.DescribeState(new EntityStateWriter(rows));
        return rows;
    }

    private static KeyValuePair<string, string> Row(string name, string value) => new(name, value);
}
