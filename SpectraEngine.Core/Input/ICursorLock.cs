namespace SpectraEngine.Core.Input;

/// <summary>
/// Requests a cursor mode from any thread. The thread that owns the window
/// applies it later, because GLFW cursor calls are window-thread affine.
/// All members are safe from any thread.
/// </summary>
public interface ICursorLock
{
    /// <summary>
    /// Asks for <paramref name="mode"/>. Returns immediately; the mode takes
    /// effect on a later pass of whichever thread owns the window.
    /// </summary>
    void RequestCursorMode(CursorMode mode);

    /// <summary>The mode applied to the window, not the one last requested.</summary>
    CursorMode CursorMode { get; }

    /// <summary>
    /// True while <see cref="CursorMode"/> is
    /// <see cref="Input.CursorMode.Locked"/>: absolute cursor position is
    /// meaningless and only relative motion may be consumed.
    /// </summary>
    bool IsCursorLocked { get; }
}
