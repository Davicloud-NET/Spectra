using SpectraEngine.Core.Audio.Captions;

namespace SpectraEngine.Bsp.Tests;

/// <summary>A caption stays up at least a second, and longer for more to read.</summary>
public sealed class CaptionTimingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Hi")]
    [InlineData("Click")]
    [InlineData("!?...")]
    public void A_few_letters_are_given_a_second(string text)
    {
        CaptionTiming.ReadingSeconds(text).ShouldBe(CaptionTiming.MinimumSeconds);
    }

    [Theory]
    [InlineData("Door opens", 9)]
    [InlineData("Stop right where you are.", 20)]
    [InlineData("Stop right\nwhere you are.", 20)]
    [InlineData("3 of 12 crates", 11)]
    [InlineData("扉が開く、そして閉まる", 10)]
    public void Each_letter_and_digit_adds_time_and_nothing_else_does(string text, int counted)
    {
        double expected = CaptionTiming.NoticeSeconds + (counted / CaptionTiming.CharactersPerSecond);

        CaptionTiming.ReadingSeconds(text).ShouldBe(expected, 1e-9);
        expected.ShouldBeGreaterThan(CaptionTiming.MinimumSeconds);
    }

    [Fact]
    public void A_long_line_is_given_as_long_as_it_takes()
    {
        string line = new('a', 150);

        CaptionTiming.ReadingSeconds(line).ShouldBe(10.5, 1e-9);
    }
}
