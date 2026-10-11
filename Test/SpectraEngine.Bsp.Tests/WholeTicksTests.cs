using SpectraEngine.Core.Entities;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The one rule that turns a wait in seconds into ticks: rounded up, a hair
/// over a whole number forgiven, and never no ticks for a wait above zero.
/// </summary>
public sealed class WholeTicksTests
{
    private const float Sixtieth = 1f / 60f;
    private const float Fiftieth = 1f / 50f;

    [Theory]
    [InlineData(0.1f, 6)]
    [InlineData(0.05f, 3)]
    [InlineData(0.25f, 15)]
    [InlineData(0.5f, 30)]
    [InlineData(1f, 60)]
    [InlineData(1.2f, 72)]
    [InlineData(3f, 180)]
    [InlineData(3600f, 216_000)]
    public void A_wait_that_is_a_whole_number_of_ticks_is_that_many_at_sixty_a_second(float seconds, long ticks)
    {
        WholeTicks.In(seconds, Sixtieth).ShouldBe(ticks);
    }

    [Theory]
    [InlineData(1f, 1)]
    [InlineData(0.5f, 1)]
    [InlineData(1.5f, 2)]
    [InlineData(2f, 2)]
    [InlineData(2.4f, 3)]
    [InlineData(59.5f, 60)]
    public void A_wait_is_rounded_up_to_the_first_tick_at_or_after_its_end(float steps, long ticks)
    {
        WholeTicks.In(steps * (double)Sixtieth, Sixtieth).ShouldBe(ticks);
    }

    [Theory]
    [InlineData(1f, 50)]
    [InlineData(0.1f, 5)]
    [InlineData(3600f, 180_000)]
    public void A_step_that_is_a_hair_short_as_a_float_adds_no_tick(float seconds, long ticks)
    {
        // 0.02f is under a fiftieth, so one second over it is a hair over 50.
        (1d / Fiftieth).ShouldBeGreaterThan(50d);

        WholeTicks.In(seconds, Fiftieth).ShouldBe(ticks);
    }

    [Theory]
    [InlineData(6.0005, 6)]
    [InlineData(6.0009, 6)]
    [InlineData(6.002, 7)]
    [InlineData(6.5, 7)]
    public void A_count_within_a_thousandth_of_a_tick_over_a_whole_number_is_that_number(double count, long ticks)
    {
        WholeTicks.In(count * 0.25, 0.25f).ShouldBe(ticks);
    }

    [Theory]
    [InlineData(100_000.05, 100_000)]
    [InlineData(100_000.2, 100_001)]
    [InlineData(10_000_000.4, 10_000_000)]
    [InlineData(10_000_000.6, 10_000_001)]
    public void A_long_wait_is_forgiven_a_millionth_of_itself_and_never_more_than_half_a_tick(
        double count, long ticks)
    {
        WholeTicks.In(count * 0.25, 0.25f).ShouldBe(ticks);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    public void No_wait_a_negative_wait_and_a_wait_that_is_no_number_are_no_ticks(double seconds)
    {
        WholeTicks.In(seconds, Sixtieth).ShouldBe(0);
    }

    [Theory]
    [InlineData(double.Epsilon)]
    [InlineData(1e-9)]
    [InlineData(0.00001)]
    [InlineData(0.0166)]
    public void Any_wait_above_zero_is_at_least_one_tick(double seconds)
    {
        WholeTicks.In(seconds, Sixtieth).ShouldBe(1);
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.MaxValue)]
    [InlineData(1e30)]
    public void A_wait_too_long_to_count_is_the_most_ticks_there_are(double seconds)
    {
        WholeTicks.In(seconds, Sixtieth).ShouldBe(long.MaxValue);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_step_that_is_not_a_time_is_refused(float step)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => WholeTicks.In(1d, step));
    }
}
