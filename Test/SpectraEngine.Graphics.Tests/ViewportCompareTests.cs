using SpectraEngine.Core.Graphics;
using System;
using Xunit;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// The comparison arithmetic behind <c>--viewport-compare</c>, with no device.
/// </summary>
public sealed class ViewportCompareTests
{
    // Linear 0.5 through an sRGB view.
    private const byte EncodedOnce = 188;

    private const byte EncodedTwice = 223;

    [Fact]
    public void Two_identical_pictures_report_no_difference_at_all()
    {
        byte[] picture = [10, 20, 30, 255, 40, 50, 60, 255];

        ViewportCompare.Reading reading = ViewportCompare.Compare(picture, picture);

        reading.MaxDelta.ShouldBe(0);
        reading.PixelCount.ShouldBe(2);
        reading.Passes.ShouldBeTrue();
    }

    [Fact]
    public void A_difference_within_the_threshold_still_passes()
    {
        // The slack is for driver rounding. A correct pair is bit-identical.
        byte[] reference = [100, 100, 100, 255];
        byte[] shared = [102, 99, 100, 255];

        ViewportCompare.Reading reading = ViewportCompare.Compare(reference, shared);

        reading.MaxDelta.ShouldBe(ViewportCompare.Threshold);
        reading.Passes.ShouldBeTrue();
    }

    [Fact]
    public void One_level_past_the_threshold_fails()
    {
        byte[] reference = [100, 100, 100, 255];
        byte[] shared = [100, 100, 103, 255];

        ViewportCompare.Reading reading = ViewportCompare.Compare(reference, shared);

        reading.MaxDelta.ShouldBe(ViewportCompare.Threshold + 1);
        reading.Passes.ShouldBeFalse();
        reading.WorstChannel.ShouldBe(ViewportCompare.Channel.Blue);
    }

    [Fact]
    public void A_double_srgb_encode_is_reported_as_a_large_delta_and_a_failure()
    {
        byte[] reference = [EncodedOnce, EncodedOnce, EncodedOnce, 255];
        byte[] shared = [EncodedTwice, EncodedTwice, EncodedTwice, 255];

        ViewportCompare.Reading reading = ViewportCompare.Compare(reference, shared);

        reading.MaxDelta.ShouldBe(EncodedTwice - EncodedOnce);
        reading.MaxDelta.ShouldBeGreaterThan(ViewportCompare.Threshold * 10);
        reading.Passes.ShouldBeFalse();
        reading.Verdict.ShouldStartWith("FAIL");
    }

    [Fact]
    public void The_worst_channel_and_texel_are_the_FIRST_ones_that_reach_the_maximum()
    {
        // Two runs of one defect must name the same pixel.
        byte[] reference = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        byte[] shared = [0, 0, 0, 0, 0, 40, 0, 0, 0, 0, 40, 0];

        ViewportCompare.Reading reading = ViewportCompare.Compare(reference, shared);

        reading.MaxDelta.ShouldBe(40);
        reading.WorstPixel.ShouldBe(1);
        reading.WorstChannel.ShouldBe(ViewportCompare.Channel.Green);
        reading.Reference.ShouldBe((byte)0);
        reading.Shared.ShouldBe((byte)40);
    }

    [Fact]
    public void Comparing_pictures_of_different_sizes_is_refused_rather_than_truncated()
    {
        byte[] four = [1, 2, 3, 4];
        byte[] eight = [1, 2, 3, 4, 5, 6, 7, 8];

        Should.Throw<ArgumentException>(() => ViewportCompare.Compare(four, eight));
    }

    [Fact]
    public void A_span_that_is_not_whole_texels_is_refused()
    {
        byte[] ragged = [1, 2, 3];

        Should.Throw<ArgumentException>(() => ViewportCompare.Compare(ragged, ragged));
    }

    [Fact]
    public void A_flat_picture_has_no_variation_and_a_drawn_one_does()
    {
        byte[] flat = [7, 8, 9, 255, 7, 8, 9, 255, 7, 8, 9, 255];
        byte[] drawn = [7, 8, 9, 255, 7, 8, 10, 255, 7, 8, 9, 255];

        ViewportCompare.HasVariation(flat).ShouldBeFalse();
        ViewportCompare.HasVariation(drawn).ShouldBeTrue();

        // A single texel has nothing to vary from.
        ViewportCompare.HasVariation([7, 8, 9, 255]).ShouldBeFalse();
    }
}
