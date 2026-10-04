namespace SpectraEngine.Core.Windowing;

/// <summary>
/// The window a <see cref="WindowModeLatch"/> drives. Window thread only.
/// </summary>
public interface IWindowModeTarget
{
    /// <summary>Position and size of the window in virtual-screen pixels.</summary>
    WindowRect Bounds { get; set; }

    /// <summary>Whether the window has the OS title bar and frame.</summary>
    bool Decorated { get; set; }

    /// <summary>
    /// Whether the window is maximized. While it is, <see cref="Bounds"/> is
    /// the restore geometry, not what is on screen.
    /// </summary>
    bool IsMaximized { get; set; }

    /// <summary>
    /// The bounds of the display the window is on. False when the backend
    /// cannot name one (headless, or the monitor went away).
    /// </summary>
    bool TryGetDisplayBounds(out WindowRect bounds);
}
