using Microsoft.Extensions.Logging;

namespace SpectraEngine.Core.Windowing;

/// <summary>
/// Holds the requested and applied window mode, and performs the
/// borderless-fullscreen transition on the window thread.
/// </summary>
public sealed class WindowModeLatch : IWindowModeLatch
{
    private readonly ILogger _logger;
    private readonly object _stateLock = new();

    private WindowMode _requestedMode = WindowMode.Windowed;
    private WindowMode _appliedMode = WindowMode.Windowed;

    // Windowed geometry to restore. Window thread only.
    private WindowRect _restoreBounds;
    private bool _restoreDecorated = true;
    private bool _restoreMaximized;

    /// <summary>Creates a latch that starts windowed.</summary>
    public WindowModeLatch(ILogger logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void RequestWindowMode(WindowMode mode)
    {
        lock (_stateLock)
            _requestedMode = mode;
    }

    /// <inheritdoc/>
    public void ToggleFullscreen()
    {
        lock (_stateLock)
        {
            _requestedMode = _requestedMode == WindowMode.Windowed
                ? WindowMode.BorderlessFullscreen
                : WindowMode.Windowed;
        }
    }

    /// <inheritdoc/>
    public WindowMode WindowMode
    {
        get { lock (_stateLock) return _appliedMode; }
    }

    /// <inheritdoc/>
    public WindowMode RequestedWindowMode
    {
        get { lock (_stateLock) return _requestedMode; }
    }

    /// <summary>
    /// Applies the last requested mode. Window thread only, once per pass of
    /// the event pump. Returns the mode newly applied, or null when nothing
    /// changed or a fullscreen request was refused for lack of display bounds.
    /// </summary>
    public WindowMode? ApplyPendingWindowMode(IWindowModeTarget target)
    {
        WindowMode requested;
        lock (_stateLock)
        {
            if (_requestedMode == _appliedMode)
                return null;
            requested = _requestedMode;
        }

        if (requested == WindowMode.BorderlessFullscreen)
        {
            if (!target.TryGetDisplayBounds(out WindowRect display) || !display.IsPositive)
            {
                _logger.LogWarning(
                    "Fullscreen request refused: the windowing backend reported no usable display bounds. Staying windowed.");
                // Reset the request so the next pass does not retry forever.
                lock (_stateLock)
                    _requestedMode = _appliedMode;
                return null;
            }

            // Capture before changing anything. A maximized window reports its
            // restore rect here.
            _restoreDecorated = target.Decorated;
            _restoreMaximized = target.IsMaximized;
            _restoreBounds = target.Bounds;

            // A maximized window ignores an explicit position and size.
            if (_restoreMaximized)
                target.IsMaximized = false;

            // Border before bounds: dropping the frame changes the client area.
            target.Decorated = false;
            target.Bounds = display;
        }
        else
        {
            target.Decorated = _restoreDecorated;
            target.Bounds = _restoreBounds;
            if (_restoreMaximized)
                target.IsMaximized = true;
        }

        lock (_stateLock)
            _appliedMode = requested;

        return requested;
    }
}
