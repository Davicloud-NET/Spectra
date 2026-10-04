namespace SpectraEngine.Core.Input;

/// <summary>
/// What the pointer looks like, independent of the windowing backend.
/// Separate from <see cref="CursorMode"/>, which is whether it is visible or captured.
/// A backend with no matching stock cursor substitutes the nearest one.
/// </summary>
public enum CursorShape
{
    /// <summary>The ordinary pointer. The resting state.</summary>
    Arrow,

    /// <summary>Precision, for a marquee.</summary>
    Crosshair,

    /// <summary>Something here can be picked up.</summary>
    Grab,

    /// <summary>Something is being dragged right now.</summary>
    Grabbing,

    /// <summary>Sizing left and right.</summary>
    SizeWestEast,

    /// <summary>Sizing up and down.</summary>
    SizeNorthSouth,

    /// <summary>Sizing along the top-left to bottom-right diagonal.</summary>
    SizeNorthWestSouthEast,

    /// <summary>Sizing along the top-right to bottom-left diagonal.</summary>
    SizeNorthEastSouthWest,

    /// <summary>Moving in every direction: a pan, an orbit, a free move.</summary>
    SizeAll,

    /// <summary>Turning. Degrades to <see cref="SizeAll"/> where the OS has no such cursor.</summary>
    Rotate,

    /// <summary>This gesture is refused: the editor is suspended.</summary>
    No,
}

/// <summary>
/// Requests a cursor shape from any thread. The thread that owns the window
/// applies it later; applying is not part of this interface.
/// </summary>
public interface ICursorShape
{
    /// <summary>Asks for <paramref name="shape"/>. Last write wins.</summary>
    void RequestCursorShape(CursorShape shape);

    /// <summary>
    /// The shape most recently requested, not the one applied. A host reads
    /// this when the OS asks what the cursor should be.
    /// </summary>
    CursorShape CursorShape { get; }
}
