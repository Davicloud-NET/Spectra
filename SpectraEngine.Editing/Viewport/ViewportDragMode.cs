namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// What a press in the viewport meant. One mode owns the pointer from press to
/// release, decided by what was under the cursor at the press: a gizmo handle,
/// then an object, then empty space.
/// </summary>
public enum ViewportDragMode
{
    /// <summary>Nothing has claimed the pointer. Camera navigation is free to run.</summary>
    None,

    /// <summary>The press landed on a gizmo handle; the manipulator owns the gesture.</summary>
    Manipulate,

    /// <summary>The press landed on empty space; a marquee is being dragged.</summary>
    BoxSelect,

    /// <summary>
    /// The press landed on an object: it was selected on the press and now
    /// follows the cursor. Ending without moving is a plain click-select.
    /// </summary>
    SelectAndMove,
}
