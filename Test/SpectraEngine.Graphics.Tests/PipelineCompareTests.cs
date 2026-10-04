using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Graphics.Tests;

/// <summary>What counts as two pipelines drawing the same picture.</summary>
public sealed class PipelineCompareTests
{
    private const int Pixels = 1000;

    [Fact]
    public void Two_identical_pictures_pass()
    {
        byte[] picture = Picture(40);

        PipelineCompare.Reading reading = PipelineCompare.Compare(picture, picture);

        reading.Passes.ShouldBeTrue();
        reading.MaxDelta.ShouldBe(0);
        reading.Outliers.ShouldBe(0);
        reading.PixelCount.ShouldBe(Pixels);
    }

    [Fact]
    public void A_difference_inside_the_tolerance_is_not_counted()
    {
        byte[] first = Picture(40);
        byte[] second = Picture(40 + PipelineCompare.Tolerance);

        PipelineCompare.Reading reading = PipelineCompare.Compare(first, second);

        reading.Passes.ShouldBeTrue();
        reading.MaxDelta.ShouldBe(PipelineCompare.Tolerance);
        reading.Outliers.ShouldBe(0);
    }

    [Fact]
    public void A_few_texels_far_out_pass_and_one_more_does_not()
    {
        // A shadow edge may land a texel either side. A whole surface lit
        // differently may not.
        int allowed = PipelineCompare.AllowedOutliers(Pixels);
        allowed.ShouldBeGreaterThan(0);

        byte[] first = Picture(40);
        byte[] second = Picture(40);
        for (int pixel = 0; pixel < allowed; pixel++)
            second[(pixel * 4) + 1] = 200;

        PipelineCompare.Compare(first, second).Passes.ShouldBeTrue();

        second[(allowed * 4) + 2] = 200;
        PipelineCompare.Reading reading = PipelineCompare.Compare(first, second);

        reading.Passes.ShouldBeFalse();
        reading.Outliers.ShouldBe(allowed + 1);
        reading.MaxDelta.ShouldBe(160);
        reading.WorstPixel.ShouldBe(0, "the first of several equal worst texels is the one reported");
    }

    [Fact]
    public void Alpha_is_not_compared()
    {
        byte[] first = Picture(40);
        byte[] second = Picture(40);
        for (int i = 3; i < second.Length; i += 4)
            second[i] = 0;

        PipelineCompare.Compare(first, second).MaxDelta.ShouldBe(0);
    }

    [Fact]
    public void Readbacks_of_different_sizes_are_refused()
    {
        Should.Throw<ArgumentException>(() => PipelineCompare.Compare(new byte[8], new byte[12]));
        Should.Throw<ArgumentException>(() => PipelineCompare.Compare(new byte[6], new byte[6]));
        Should.Throw<ArgumentException>(() => PipelineCompare.Compare([], []));
    }

    private static byte[] Picture(int level)
    {
        var picture = new byte[Pixels * 4];
        for (int i = 0; i < picture.Length; i++)
            picture[i] = i % 4 == 3 ? (byte)255 : (byte)level;
        return picture;
    }
}
