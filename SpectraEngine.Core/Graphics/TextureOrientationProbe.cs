namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Checks that the backends agree which way up a texture is, using a fixture
/// with four differently coloured quadrants. Quadrants are named as an image
/// viewer shows the file.
/// </summary>
public static class TextureOrientationProbe
{
    /// <summary>Content-relative path of the fixture.</summary>
    public const string TexturePath = "Textures/orientation_probe.png";

    /// <summary>One of the fixture's four quadrant colours, or none of them.</summary>
    public enum Quadrant
    {
        Unrecognised,

        /// <summary>Top-left in the authored image.</summary>
        Red,

        /// <summary>Top-right in the authored image.</summary>
        Green,

        /// <summary>Bottom-left in the authored image.</summary>
        Blue,

        /// <summary>Bottom-right in the authored image.</summary>
        Yellow,
    }

    // The readback went through the tone curve: a full channel lands near 205.
    private const int On = 120;
    private const int Off = 80;

    /// <summary>Names the quadrant a read-back texel came from, or <see cref="Quadrant.Unrecognised"/>.</summary>
    public static Quadrant Classify(byte r, byte g, byte b)
    {
        bool red = r >= On, green = g >= On, blue = b >= On;
        bool noRed = r <= Off, noGreen = g <= Off, noBlue = b <= Off;

        if (red && noGreen && noBlue) return Quadrant.Red;
        if (noRed && green && noBlue) return Quadrant.Green;
        if (noRed && noGreen && blue) return Quadrant.Blue;
        if (red && green && noBlue) return Quadrant.Yellow;
        return Quadrant.Unrecognised;
    }

    /// <summary>
    /// The four corners of the rendered picture, named as the picture is seen,
    /// not as a buffer stores it.
    /// </summary>
    public readonly record struct Reading(
        Quadrant TopLeft, Quadrant TopRight, Quadrant BottomLeft, Quadrant BottomRight)
    {
        /// <summary>True when the picture shows the file the way an image viewer shows it.</summary>
        public bool MatchesAuthoredImage =>
            TopLeft == Quadrant.Red && TopRight == Quadrant.Green &&
            BottomLeft == Quadrant.Blue && BottomRight == Quadrant.Yellow;

        /// <summary>True when the picture is the authored image mirrored top to bottom.</summary>
        public bool IsVerticallyFlipped =>
            TopLeft == Quadrant.Blue && TopRight == Quadrant.Yellow &&
            BottomLeft == Quadrant.Red && BottomRight == Quadrant.Green;

        public override string ToString() =>
            $"top-left {TopLeft}, top-right {TopRight}, bottom-left {BottomLeft}, bottom-right {BottomRight}";

        /// <summary>Upright, flipped, or neither, as a sentence for a log.</summary>
        public string Verdict => MatchesAuthoredImage
            ? "UPRIGHT (the picture matches the authored image)"
            : IsVerticallyFlipped
                ? "FLIPPED vertically (the authored top row rendered at the bottom)"
                : "UNEXPECTED (neither upright nor a plain vertical flip)";
    }
}
