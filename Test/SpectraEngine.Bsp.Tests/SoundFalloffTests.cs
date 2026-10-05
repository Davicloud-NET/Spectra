using SpectraEngine.Core.Audio.Propagation;

namespace SpectraEngine.Bsp.Tests;

public sealed class SoundFalloffTests
{
    private const float Min = 2f;
    private const float Max = 30f;

    [Fact]
    public void A_sound_is_at_full_volume_up_to_its_first_distance()
    {
        SoundFalloff.Gain(0f, Min, Max).ShouldBe(1f);
        SoundFalloff.Gain(1f, Min, Max).ShouldBe(1f);
        SoundFalloff.Gain(Min, Min, Max).ShouldBe(1f);
    }

    [Fact]
    public void A_sound_is_silent_at_its_second_distance_and_beyond()
    {
        SoundFalloff.Gain(Max, Min, Max).ShouldBe(0f);
        SoundFalloff.Gain(Max + 0.001f, Min, Max).ShouldBe(0f);
        SoundFalloff.Gain(1000f, Min, Max).ShouldBe(0f);
        SoundFalloff.Gain(float.PositiveInfinity, Min, Max).ShouldBe(0f);
    }

    [Fact]
    public void Between_the_two_distances_the_gain_is_the_inverse_curve_tapered_to_zero()
    {
        // Halfway: the inverse curve gives 2/16 and the taper halves it.
        SoundFalloff.Gain(16f, Min, Max).ShouldBe(0.0625f, 1e-6f);

        // The plain inverse at twice the first distance is a half. The taper takes a little off.
        float atDouble = SoundFalloff.Gain(4f, Min, Max);
        atDouble.ShouldBe(0.5f * (26f / 28f), 1e-6f);
    }

    [Fact]
    public void The_curve_has_no_step_at_either_distance()
    {
        SoundFalloff.Gain(Min + 0.001f, Min, Max).ShouldBe(1f, 0.001f);
        SoundFalloff.Gain(Max - 0.001f, Min, Max).ShouldBe(0f, 0.001f);
    }

    [Fact]
    public void Further_away_is_never_louder()
    {
        float previous = 1f;
        for (int step = 0; step <= 4000; step++)
        {
            float gain = SoundFalloff.Gain(step * 0.01f, Min, Max);

            gain.ShouldBeInRange(0f, 1f);
            gain.ShouldBeLessThanOrEqualTo(previous);
            previous = gain;
        }

        previous.ShouldBe(0f);
    }

    [Fact]
    public void A_second_distance_at_or_below_the_first_is_a_hard_edge_at_the_first()
    {
        SoundFalloff.Gain(5f, minDistance: 5f, maxDistance: 5f).ShouldBe(1f);
        SoundFalloff.Gain(5.001f, minDistance: 5f, maxDistance: 5f).ShouldBe(0f);

        SoundFalloff.Gain(4f, minDistance: 5f, maxDistance: 3f).ShouldBe(1f);
        SoundFalloff.Gain(6f, minDistance: 5f, maxDistance: 3f).ShouldBe(0f);
    }

    [Fact]
    public void Negative_distances_count_as_zero()
    {
        SoundFalloff.Gain(-3f, Min, Max).ShouldBe(1f);

        // A first distance of zero leaves full volume at the emitter only.
        SoundFalloff.Gain(0f, minDistance: -1f, maxDistance: 10f).ShouldBe(1f);
        SoundFalloff.Gain(1f, minDistance: -1f, maxDistance: 10f).ShouldBe(0f);

        SoundFalloff.Gain(1f, minDistance: 0f, maxDistance: -10f).ShouldBe(0f);
    }

    [Fact]
    public void A_distance_that_is_not_a_number_is_silence()
    {
        SoundFalloff.Gain(float.NaN, Min, Max).ShouldBe(0f);
        SoundFalloff.Gain(5f, float.NaN, Max).ShouldBe(0f);
        SoundFalloff.Gain(5f, Min, float.NaN).ShouldBe(0f);
    }

    [Fact]
    public void An_infinite_second_distance_is_the_plain_inverse_curve()
    {
        SoundFalloff.Gain(4f, Min, float.PositiveInfinity).ShouldBe(0.5f);
        SoundFalloff.Gain(200f, Min, float.PositiveInfinity).ShouldBe(0.01f, 1e-7f);
        SoundFalloff.Gain(float.PositiveInfinity, Min, float.PositiveInfinity).ShouldBe(0f);
    }
}
