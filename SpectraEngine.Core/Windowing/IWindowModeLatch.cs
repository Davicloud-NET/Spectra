namespace SpectraEngine.Core.Windowing;

/// <summary>
/// Requests a window mode from any thread. The thread that owns the window
/// applies it later.
/// </summary>
// Window calls are window-thread affine (GLFW), and F11 is read on the render
// thread. The engine owns fullscreen because DXGI's own Alt+Enter transition
// runs on the main thread while the render thread uses the same swap chain.
public interface IWindowModeLatch
{
    /// <summary>Asks for <paramref name="mode"/>. Takes effect on a later pass of the window thread.</summary>
    void RequestWindowMode(WindowMode mode);

    /// <summary>Flips the requested mode, so two toggles before an apply cancel out.</summary>
    void ToggleFullscreen();

    /// <summary>The mode applied to the window, not the one last requested.</summary>
    WindowMode WindowMode { get; }

    /// <summary>The mode last asked for, applied or not.</summary>
    WindowMode RequestedWindowMode { get; }
}
