using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// How hard a sound steps when the listener walks past a door frame: the
/// largest change from one frame to the next in what the propagation asks
/// for, and in what the smoother then plays. Printed for one line and for
/// several, and bounded for the lines the engine uses.
/// </summary>
// The wall is half a unit of brick at z from -4.5 to -4, with a doorway at x
// from -1 to 1. The sound is 3.5 units behind it, in line with the doorway,
// and at full volume all the way, so a step is the wall's alone. The listener
// walks along the wall 3 units in front of it, and the line to the sound
// leaves the doorway at x = 1.75.
public sealed class WallPropagationPopTests
{
    // The character's walking speed, and a slow walk.
    private const float Walk = 4.5f;
    private const float Stroll = 1.4f;

    private const string TableSwitch = "SPECTRA_WALL_POPS";

    private static readonly Vector3 Sound = new(0f, 1.5f, -8f);
    private static readonly WallPropagationSettings Engine = WallPropagationSettings.Default;

    [Theory]
    [InlineData(Stroll, 0.25f, 0.06f)]
    [InlineData(Walk, 0.4f, 0.1f)]
    public void The_engines_lines_keep_a_door_frame_to_a_step_this_small(float speed, float asked, float played)
    {
        Steps steps = WalkPast(Engine, speed);

        steps.Asked.Gain.ShouldBeLessThan(asked);
        steps.Played.Gain.ShouldBeLessThan(played);
        steps.Played.GainHf.ShouldBeLessThanOrEqualTo(SoundPathSmoother.MaxGainHfStep + 1e-6f);
    }

    [Theory]
    [InlineData(Stroll)]
    [InlineData(Walk)]
    public void One_line_steps_by_the_whole_wall_in_a_frame(float speed)
    {
        Steps one = WalkPast(Engine with { Lines = 1 }, speed);
        Steps several = WalkPast(Engine, speed);

        // The line meets the wall at its corner first, where it is thin.
        one.Asked.Gain.ShouldBeGreaterThan(0.7f);
        one.Played.Gain.ShouldBeGreaterThan(0.11f);
        several.Asked.Gain.ShouldBeLessThan(one.Asked.Gain * 0.5f);
        several.Played.Gain.ShouldBeLessThan(one.Played.Gain * 0.7f);
    }

    // Opt-in: it checks nothing and walks the level 42 times. For choosing
    // the lines and the ring by its table:
    //   PowerShell:  $env:SPECTRA_WALL_POPS = "1"
    //   bash:        export SPECTRA_WALL_POPS=1
    //   dotnet run --project Test/SpectraEngine.Bsp.Tests -- -class "SpectraEngine.Bsp.Tests.WallPropagationPopTests" -showLiveOutput
    [Fact]
    public void The_steps_for_every_count_of_lines_and_every_ring_are_printed()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(TableSwitch) == "1",
            $"Opt-in: set {TableSwitch}=1 to print the table.");

        var output = TestContext.Current.TestOutputHelper.ShouldNotBeNull();
        output.WriteLine("Largest step in one frame, of a full scale of 1.");
        output.WriteLine("lines  ring   asked gain  asked high end  played gain  played high end");

        foreach (float speed in (float[])[Stroll, Walk])
        {
            output.WriteLine(FormattableString.Invariant($"walking at {speed:0.0} units a second"));
            output.WriteLine($"  1    none   {WalkPast(Engine with { Lines = 1 }, speed)}");

            foreach (int lines in (int[])[3, 5, 7, 9])
            {
                foreach (float radius in (float[])[0.1f, 0.175f, 0.25f, 0.35f, 0.5f])
                {
                    Steps steps = WalkPast(Engine with { Lines = lines, HeadRadius = radius }, speed);
                    output.WriteLine(FormattableString.Invariant($"  {lines}    {radius:0.000}  {steps}"));
                }
            }
        }
    }

    // Walks the listener from behind the wall into the doorway's view and out
    // the other side.
    private static Steps WalkPast(WallPropagationSettings settings, float speed)
    {
        var level = new SpanLevel();
        level.Wall();
        level.Doorway();
        level.Compile();

        var materials = new FakeAcousticMaterials().Add(SpanLevel.Brick, AcousticPresets.Brick);
        var rig = new WallRig(new SceneSoundObstacles(() => level.Scene), materials, settings);
        int sound = rig.Add(Sound, minDistance: 20f);

        var smoother = new SoundPathSmoother();
        var steps = default(Steps);
        SoundPath lastAsked = default;
        SoundPath lastPlayed = default;

        int frames = (int)(8f / (speed * WallRig.FrameSeconds));
        for (int frame = 0; frame <= frames; frame++)
        {
            rig.Listener = new Vector3(-4f + (frame * speed * WallRig.FrameSeconds), 1.6f, -1f);
            rig.Frame();

            SoundPath asked = rig.Heard(sound);
            smoother.Step(in asked, WallRig.FrameSeconds);
            var played = new SoundPath(asked.Position, smoother.Gain, smoother.GainHf);

            if (frame > 0)
                steps = new Steps(steps.Asked.Widened(lastAsked, asked), steps.Played.Widened(lastPlayed, played));

            lastAsked = asked;
            lastPlayed = played;
        }

        return steps;
    }

    private readonly record struct Step(float Gain, float GainHf)
    {
        public Step Widened(SoundPath from, SoundPath to) => new(
            MathF.Max(Gain, MathF.Abs(to.Gain - from.Gain)),
            MathF.Max(GainHf, MathF.Abs(to.GainHf - from.GainHf)));
    }

    private readonly record struct Steps(Step Asked, Step Played)
    {
        public override string ToString() => FormattableString.Invariant(
            $"{Asked.Gain:0.000}       {Asked.GainHf:0.000}           {Played.Gain:0.000}        {Played.GainHf:0.000}");
    }
}
