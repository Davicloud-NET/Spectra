using SpectraEngine.Core.Audio.Propagation;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

public sealed class SoundPathSmootherTests
{
    private const float Frame = 1f / 60f;

    [Fact]
    public void The_first_sample_lands_on_its_target()
    {
        var smoother = new SoundPathSmoother();

        smoother.Step(Target(0.8f, 0.3f), Frame);

        smoother.Gain.ShouldBe(0.8f);
        smoother.GainHf.ShouldBe(0.3f);
    }

    [Fact]
    public void A_change_is_spread_over_time_and_arrives()
    {
        SoundPathSmoother smoother = StartedAt(1f, 1f);

        smoother.Step(Target(0f, 0.2f), Frame);

        // One frame in, most of the change is still to come.
        smoother.Gain.ShouldBeInRange(0.8f, 0.99f);
        smoother.GainHf.ShouldBeInRange(0.84f, 0.99f);

        for (int i = 0; i < 120; i++) smoother.Step(Target(0f, 0.2f), Frame);

        smoother.Gain.ShouldBe(0f);
        smoother.GainHf.ShouldBe(0.2f);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void A_fade_lands_on_its_target_and_does_not_stop_a_hair_short(int framesPerSecond)
    {
        // What decides "can it be heard" compares the gain with zero.
        SoundPathSmoother smoother = StartedAt(1f, 1f);

        for (int i = 0; i < framesPerSecond * 2; i++) smoother.Step(Target(0f, 0.3f), 1f / framesPerSecond);

        smoother.Gain.ShouldBe(0f);
        smoother.GainHf.ShouldBe(0.3f);
    }

    [Fact]
    public void A_change_too_small_to_hear_is_made_at_once()
    {
        SoundPathSmoother smoother = StartedAt(0.5f, 0.5f);

        smoother.Step(Target(0.50005f, 0.49995f), Frame);

        smoother.Gain.ShouldBe(0.50005f);
        smoother.GainHf.ShouldBe(0.49995f);
    }

    [Fact]
    public void One_time_constant_covers_about_two_thirds_of_a_change()
    {
        SoundPathSmoother smoother = StartedAt(0f, 1f);

        smoother.Step(Target(1f, 1f), SoundPathSmoother.DefaultTimeConstant);

        smoother.Gain.ShouldBe(1f - MathF.Exp(-1f), 1e-5f);
    }

    [Fact]
    public void Two_half_steps_land_where_one_whole_step_does()
    {
        SoundPathSmoother whole = StartedAt(1f, 1f);
        SoundPathSmoother halves = StartedAt(1f, 1f);

        // The high end moves too little here for its step limit to matter.
        whole.Step(Target(0f, 0.99f), 0.04f);
        halves.Step(Target(0f, 0.99f), 0.02f);
        halves.Step(Target(0f, 0.99f), 0.02f);

        halves.Gain.ShouldBe(whole.Gain, 1e-5f);
        halves.GainHf.ShouldBe(whole.GainHf, 1e-5f);
    }

    [Theory]
    [InlineData(1f / 240f)]
    [InlineData(1f / 60f)]
    [InlineData(1f / 30f)]
    [InlineData(0.5f)]
    public void The_high_end_gain_never_moves_further_than_its_step_limit(float deltaSeconds)
    {
        SoundPathSmoother falling = StartedAt(1f, 1f);
        SoundPathSmoother rising = StartedAt(1f, 0f);

        for (int i = 0; i < 600; i++)
        {
            float wasFalling = falling.GainHf;
            float wasRising = rising.GainHf;

            falling.Step(Target(1f, 0f), deltaSeconds);
            rising.Step(Target(1f, 1f), deltaSeconds);

            (wasFalling - falling.GainHf).ShouldBeInRange(0f, SoundPathSmoother.MaxGainHfStep + 1e-6f);
            (rising.GainHf - wasRising).ShouldBeInRange(0f, SoundPathSmoother.MaxGainHfStep + 1e-6f);
        }

        falling.GainHf.ShouldBe(0f);
        rising.GainHf.ShouldBe(1f);
    }

    [Fact]
    public void The_step_limit_leaves_the_gain_alone()
    {
        SoundPathSmoother smoother = StartedAt(1f, 1f);

        smoother.Step(Target(0f, 0f), Frame);

        smoother.Gain.ShouldBe(MathF.Exp(-Frame / SoundPathSmoother.DefaultTimeConstant), 1e-5f);
        smoother.GainHf.ShouldBe(1f - SoundPathSmoother.MaxGainHfStep, 1e-6f);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(240)]
    public void The_high_end_gain_goes_as_far_in_half_a_second_at_any_frame_rate_from_60_up(int framesPerSecond)
    {
        SoundPathSmoother smoother = StartedAt(1f, 1f);

        for (int i = 0; i < framesPerSecond / 2; i++) smoother.Step(Target(1f, 0.1f), 1f / framesPerSecond);

        smoother.GainHf.ShouldBe(1f - (SoundPathSmoother.MaxGainHfRate / 2f), 1e-3f);
    }

    [Fact]
    public void A_sound_takes_three_quarters_of_a_second_to_go_dull()
    {
        // The price of the step limit. Without it this is 0.3 s.
        SoundPathSmoother smoother = StartedAt(1f, 1f);
        int steps = 0;

        while (smoother.GainHf - 0.1f > 0.05f * 0.9f)
        {
            smoother.Step(Target(1f, 0.1f), Frame);
            steps++;
        }

        steps.ShouldBe(45);
    }

    [Fact]
    public void The_high_end_gain_of_a_silent_path_lands_at_once()
    {
        SoundPathSmoother smoother = StartedAt(0f, 1f);

        smoother.Step(Target(1f, 0.2f), Frame);

        smoother.Gain.ShouldBeInRange(0.1f, 0.2f);
        smoother.GainHf.ShouldBe(0.2f);
    }

    [Theory]
    [InlineData(0.0001f)]
    [InlineData(1f / 240f)]
    [InlineData(1f / 60f)]
    [InlineData(0.1f)]
    [InlineData(1f)]
    [InlineData(1000f)]
    public void The_shown_value_never_passes_its_target(float deltaSeconds)
    {
        SoundPathSmoother falling = StartedAt(1f, 1f);
        SoundPathSmoother rising = StartedAt(0f, 0f);

        float lastFalling = 1f;
        float lastRising = 0f;

        for (int i = 0; i < 400; i++)
        {
            falling.Step(Target(0.3f, 0.3f), deltaSeconds);
            rising.Step(Target(0.7f, 0.7f), deltaSeconds);

            falling.Gain.ShouldBeInRange(0.3f, lastFalling);
            rising.Gain.ShouldBeInRange(lastRising, 0.7f);

            lastFalling = falling.Gain;
            lastRising = rising.Gain;
        }
    }

    [Fact]
    public void A_teleport_lands_on_the_target_at_once()
    {
        SoundPathSmoother smoother = StartedAt(1f, 1f);

        smoother.Step(Target(0.1f, 0.4f), Frame, jumped: true);

        smoother.Gain.ShouldBe(0.1f);
        smoother.GainHf.ShouldBe(0.4f);
    }

    [Fact]
    public void A_reset_smoother_lands_on_its_next_target()
    {
        SoundPathSmoother smoother = StartedAt(1f, 1f);

        smoother.Reset();
        smoother.Step(Target(0.2f, 0.6f), Frame);

        smoother.Gain.ShouldBe(0.2f);
        smoother.GainHf.ShouldBe(0.6f);
    }

    [Fact]
    public void No_time_passing_moves_nothing()
    {
        SoundPathSmoother smoother = StartedAt(1f, 1f);

        smoother.Step(Target(0f, 0f), 0f);
        smoother.Step(Target(0f, 0f), -1f);
        smoother.Step(Target(0f, 0f), float.NaN);

        smoother.Gain.ShouldBe(1f);
        smoother.GainHf.ShouldBe(1f);
    }

    [Fact]
    public void A_time_constant_of_zero_is_no_smoothing()
    {
        SoundPathSmoother smoother = StartedAt(1f, 1f);

        smoother.Step(Target(0.5f, 0.25f), Frame, timeConstant: 0f);

        smoother.Gain.ShouldBe(0.5f);
        smoother.GainHf.ShouldBe(0.25f);
    }

    [Fact]
    public void A_target_that_is_not_a_number_is_treated_as_silence()
    {
        SoundPathSmoother smoother = StartedAt(1f, 1f);

        smoother.Step(Target(float.NaN, float.NaN), Frame, jumped: true);
        smoother.Gain.ShouldBe(0f);
        smoother.GainHf.ShouldBe(0f);

        // The state is still usable afterwards.
        smoother.Step(Target(1f, 1f), Frame, jumped: true);
        smoother.Gain.ShouldBe(1f);
    }

    [Fact]
    public void An_infinite_target_is_treated_as_silence()
    {
        SoundPathSmoother smoother = StartedAt(1f, 1f);
        SoundPath endless = Target(float.PositiveInfinity, float.NegativeInfinity);

        // With no time passing the difference times zero must not become NaN.
        smoother.Step(endless, 0f);
        smoother.Step(endless, float.NaN);
        smoother.Gain.ShouldBe(1f);
        smoother.GainHf.ShouldBe(1f);

        smoother.Step(endless, Frame);
        smoother.Gain.ShouldBeInRange(0.8f, 0.99f);
        smoother.GainHf.ShouldBeInRange(0.8f, 0.99f);

        smoother.Step(endless, Frame, jumped: true);
        smoother.Gain.ShouldBe(0f);
        smoother.GainHf.ShouldBe(0f);
    }

    [Fact]
    public void An_endless_time_step_over_an_endless_time_constant_moves_nothing()
    {
        SoundPathSmoother smoother = StartedAt(1f, 1f);

        smoother.Step(Target(0f, 0f), float.PositiveInfinity, timeConstant: float.PositiveInfinity);

        smoother.Gain.ShouldBe(1f);
        smoother.GainHf.ShouldBe(1f);

        // The state is still usable afterwards.
        smoother.Step(Target(0f, 0f), Frame);
        smoother.Gain.ShouldBeInRange(0.8f, 0.99f);
    }

    private static SoundPath Target(float gain, float gainHf) => new(Vector3.Zero, gain, gainHf);

    private static SoundPathSmoother StartedAt(float gain, float gainHf)
    {
        var smoother = new SoundPathSmoother();
        smoother.Step(Target(gain, gainHf), Frame);
        return smoother;
    }
}
