using SpectraEngine.Core.Audio.Propagation;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

public sealed class DirectPropagationTests
{
    private static readonly SoundListener Listener = new(new Vector3(0, 1, 0), -Vector3.UnitZ, Vector3.UnitY);

    [Fact]
    public void Every_emitter_gets_one_unmuffled_path_at_its_own_position()
    {
        SoundQuery[] emitters =
        [
            new(new Vector3(0, 1, -1), MinDistance: 2f, MaxDistance: 30f),
            new(new Vector3(16, 1, 0), MinDistance: 2f, MaxDistance: 30f),
            new(new Vector3(0, 1, 100), MinDistance: 2f, MaxDistance: 30f),
        ];
        var results = new SoundPaths[emitters.Length];

        new DirectPropagation().Resolve(Listener, emitters, results);

        for (int i = 0; i < emitters.Length; i++)
        {
            results[i].Count.ShouldBe(1);
            results[i][0].Position.ShouldBe(emitters[i].Position);
            results[i][0].GainHf.ShouldBe(1f);
        }

        results[0][0].Gain.ShouldBe(1f);
        results[1][0].Gain.ShouldBe(SoundFalloff.Gain(16f, 2f, 30f));
        results[2][0].Gain.ShouldBe(0f);
    }

    [Fact]
    public void The_gain_follows_the_distance_to_the_listener_and_not_to_the_origin()
    {
        SoundQuery[] emitters = [new(new Vector3(100, 1, 0), MinDistance: 2f, MaxDistance: 30f)];
        var results = new SoundPaths[1];
        var beside = new SoundListener(new Vector3(99, 1, 0), Vector3.UnitX, Vector3.UnitY);

        new DirectPropagation().Resolve(beside, emitters, results);

        results[0][0].Gain.ShouldBe(1f);
    }

    [Fact]
    public void An_emitter_that_does_not_fade_arrives_at_full_volume_from_any_distance()
    {
        SoundSimulation unfading = SoundSimulation.All & ~SoundSimulation.Fades;
        SoundQuery[] emitters =
        [
            new(new Vector3(0, 1, -1), MinDistance: 2f, MaxDistance: 30f, unfading),
            new(new Vector3(16, 1, 0), MinDistance: 2f, MaxDistance: 30f, unfading),
            new(new Vector3(0, 1, 100), MinDistance: 2f, MaxDistance: 30f, unfading),
            new(new Vector3(0, 1, 100), MinDistance: 2f, MaxDistance: 30f, SoundSimulation.Fades),
        ];
        var results = new SoundPaths[emitters.Length];

        new DirectPropagation().Resolve(Listener, emitters, results);

        results[0][0].Gain.ShouldBe(1f);
        results[1][0].Gain.ShouldBe(1f);
        results[2][0].Gain.ShouldBe(1f);
        results[2][0].Position.ShouldBe(emitters[2].Position);
        results[3][0].Gain.ShouldBe(0f);
    }

    [Fact]
    public void Results_past_the_last_emitter_are_left_alone()
    {
        SoundQuery[] emitters = [new(Vector3.Zero, MinDistance: 2f, MaxDistance: 30f)];
        var marker = new SoundPaths(new SoundPath(Vector3.One, 0.25f, 0.5f));
        SoundPaths[] results = [marker, marker];

        new DirectPropagation().Resolve(Listener, emitters, results);

        results[1][0].ShouldBe(new SoundPath(Vector3.One, 0.25f, 0.5f));
    }

    [Fact]
    public void No_emitters_leaves_the_results_alone()
    {
        var kept = new SoundPaths(new SoundPath(Vector3.One, 0.25f, 0.5f));
        SoundPaths[] results = [kept];

        new DirectPropagation().Resolve(Listener, [], results);

        results[0].Count.ShouldBe(1);
        results[0][0].ShouldBe(new SoundPath(Vector3.One, 0.25f, 0.5f));
    }

    [Fact]
    public void Too_few_results_for_the_emitters_is_refused()
    {
        var emitters = new SoundQuery[3];
        var results = new SoundPaths[2];

        Should.Throw<ArgumentException>(() => new DirectPropagation().Resolve(Listener, emitters, results));
    }
}
