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

        smoother.Gain.ShouldBe(0f, 1e-4f);
        smoother.GainHf.ShouldBe(0.2f, 1e-4f);
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

        whole.Step(Target(0f, 0f), 0.04f);
        halves.Step(Target(0f, 0f), 0.02f);
        halves.Step(Target(0f, 0f), 0.02f);

        halves.Gain.ShouldBe(whole.Gain, 1e-5f);
        halves.GainHf.ShouldBe(whole.GainHf, 1e-5f);
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

    private static SoundPath Target(float gain, float gainHf) => new(Vector3.Zero, gain, gainHf);

    private static SoundPathSmoother StartedAt(float gain, float gainHf)
    {
        var smoother = new SoundPathSmoother();
        smoother.Step(Target(gain, gainHf), Frame);
        return smoother;
    }
}
