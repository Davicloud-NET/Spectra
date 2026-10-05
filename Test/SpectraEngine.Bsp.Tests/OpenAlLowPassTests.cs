using SpectraEngine.Core.Audio;

namespace SpectraEngine.Bsp.Tests;

// The part of the filter that needs no audio library. The rest is in
// OpenAlLowPassLoopbackTests.
public sealed class OpenAlLowPassTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.25f, 0.25f)]
    [InlineData(1f, 1f)]
    [InlineData(-0.5f, 0f)]
    [InlineData(1.5f, 1f)]
    [InlineData(float.NegativeInfinity, 0f)]
    [InlineData(float.PositiveInfinity, 1f)]
    public void A_gain_hf_outside_zero_to_one_is_clamped(float given, float used)
    {
        OpenAlLowPass.Clamp(given).ShouldBe(used);
    }

    [Fact]
    public void A_gain_hf_that_is_not_a_number_counts_as_unfiltered()
    {
        // OpenAL refuses a NaN and keeps the old value, so the last sound's
        // filter would stay on the source.
        OpenAlLowPass.Clamp(float.NaN).ShouldBe(1f);
    }
}
