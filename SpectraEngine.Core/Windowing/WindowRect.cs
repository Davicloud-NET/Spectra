namespace SpectraEngine.Core.Windowing;

/// <summary>
/// A window or display rectangle in virtual-screen pixels, top-left origin,
/// y down.
/// </summary>
public readonly record struct WindowRect(int X, int Y, int Width, int Height)
{
    /// <summary>True when the rectangle covers at least one pixel in both axes.</summary>
    public bool IsPositive => Width > 0 && Height > 0;
}
