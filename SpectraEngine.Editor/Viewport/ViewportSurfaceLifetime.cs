namespace SpectraEngine.Editor.Viewport;

/// <summary>What an attach means for the surface the shell builds an engine on.</summary>
public enum ViewportAttach
{
    /// <summary>The first attach of a session: publish a surface and start an engine on it.</summary>
    Publish,

    /// <summary>A re-parent. The surface is still live, so there is no session to start.</summary>
    Resume,

    /// <summary>An attach after shutdown. Publishing would start a second engine.</summary>
    Ignore,
}

/// <summary>
/// Tracks whether a viewport's surface is live, so a detach is a re-parent
/// until <see cref="Shutdown"/> is called. UI thread only.
/// </summary>
// Docking or floating a control detaches and re-attaches it. Treated as a
// teardown, that stops the engine and builds a second session with a new scene.
public sealed class ViewportSurfaceLifetime
{
    private bool _published;
    private bool _shuttingDown;

    /// <summary>Whether the shell holds a surface from this viewport with an engine on it.</summary>
    public bool IsPublished => _published;

    /// <summary>Whether the shell has said this viewport is finished.</summary>
    public bool IsShuttingDown => _shuttingDown;

    /// <summary>What this attach means. Call it where the surface would be published.</summary>
    // Not at control attach: the compositor negotiation is async, and a viewport
    // detached again in that gap must publish nothing.
    public ViewportAttach Attached()
    {
        if (_shuttingDown)
            return ViewportAttach.Ignore;

        if (_published)
            return ViewportAttach.Resume;

        _published = true;
        return ViewportAttach.Publish;
    }

    /// <summary>
    /// Whether this detach must raise the destroy event. True only after
    /// <see cref="Shutdown"/>, and only once.
    /// </summary>
    public bool Detached() => _shuttingDown && TakePublished();

    /// <summary>
    /// The shell is finished with this viewport. Returns whether the destroy
    /// event is owed now.
    /// </summary>
    // Owed here, before the control leaves the tree: the engine must stop while
    // the compositor half is still whole, or the keyed-mutex hand-over hangs.
    public bool Shutdown()
    {
        _shuttingDown = true;
        return TakePublished();
    }

    private bool TakePublished()
    {
        if (!_published)
            return false;

        _published = false;
        return true;
    }
}
