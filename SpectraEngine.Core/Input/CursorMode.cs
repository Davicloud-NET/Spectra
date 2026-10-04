namespace SpectraEngine.Core.Input;

/// <summary>
/// How the mouse cursor is presented over the window, independent of the
/// windowing backend. Requested through <see cref="ICursorLock"/>.
/// </summary>
public enum CursorMode
{
    /// <summary>Visible, free to leave the window. The resting state.</summary>
    Normal,

    /// <summary>
    /// Invisible but still an ordinary cursor: absolute position stays
    /// meaningful and it can still leave the window.
    /// </summary>
    Hidden,

    /// <summary>
    /// Invisible and captured: only relative motion is meaningful, and
    /// <see cref="SpectraEngine.Core.Input.InputManager.MousePosition"/> freezes.
    /// </summary>
    Locked,
}
