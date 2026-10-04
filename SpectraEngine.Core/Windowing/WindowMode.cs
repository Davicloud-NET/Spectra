namespace SpectraEngine.Core.Windowing;

/// <summary>How the engine's window covers the display.</summary>
// No exclusive fullscreen: with a flip-model swap chain borderless is
// equivalent, and it avoids mode switches and device loss on alt-tab.
public enum WindowMode
{
    /// <summary>An ordinary decorated, resizable window.</summary>
    Windowed,

    /// <summary>Undecorated and sized to fill the display the window is on.</summary>
    BorderlessFullscreen,
}
