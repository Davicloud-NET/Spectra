namespace SpectraEngine.Editor.Viewport;

// Integer: the locked move recognises its own re-pin echo by a zero delta,
// which needs equality without rounding.
internal readonly record struct ViewportPoint(int X, int Y);

internal readonly record struct ViewportSize(int Width, int Height);

// The platform half of the viewport's input path. Policy lives in
// ViewportInputRouter; this is the part a new host writes.
// Window thread only.
internal interface IViewportCursor
{
    // Also the framebuffer size.
    ViewportSize ClientSize { get; }

    // How far a press may travel and still be a click, at this window's DPI.
    // Read per press: a window can move between monitors.
    int DragSlack { get; }

    ViewportPoint ClientToScreen(ViewportPoint client);

    void MoveCursor(int screenX, int screenY);

    // Whole client rect, not a band around the anchor: a tight fence leaves
    // less travel to survive a UI stall.
    void ClipToClient(bool clip);

    void SetCursorHidden(bool hidden);

    // Capture keeps a drag that leaves the viewport ending on release.
    void SetPointerCapture(bool captured);
}
