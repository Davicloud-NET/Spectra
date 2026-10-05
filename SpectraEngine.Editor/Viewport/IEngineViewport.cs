using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SpectraEngine.Editor.Viewport;

/// <summary>
/// The pane the engine renders into, native child or composited. UI thread only.
/// </summary>
public interface IEngineViewport
{
    /// <summary>
    /// Raised on the UI thread once the render surface exists. A host starts
    /// the engine here.
    /// </summary>
    // The surface does not exist until the control is attached to a visual tree.
    event Action<IRenderSurface>? SurfaceCreated;

    /// <summary>
    /// Raised on the UI thread before the render surface goes away. A host must
    /// have stopped the engine by the time this returns.
    /// </summary>
    event Action? SurfaceDestroying;

    /// <summary>
    /// Raised for a Ctrl chord the shell owns rather than the engine.
    /// </summary>
    event Action<ShellChord>? ShellChord;

    /// <summary>
    /// Raised on the UI thread for a right-click that never became a freelook
    /// drag, in framebuffer pixels. The engine has already seen the balanced
    /// button events.
    /// </summary>
    event Action<int, int>? ContextMenuRequested;

    /// <summary>
    /// Raised on the UI thread when an asset is dropped into the viewport, in
    /// framebuffer pixels. Never raised when <see cref="AcceptsAssetDrops"/> is
    /// false.
    /// </summary>
    // The scope is read from the modifier keys at the drop, not remembered
    // from the last DragOver.
    event Action<ContentDragPayload, int, int, MaterialDropScope>? AssetDropped;

    /// <summary>
    /// Raised on the UI thread with the asset being dragged over this viewport,
    /// or null when there is none. Raised only on a change, and never when
    /// <see cref="AcceptsAssetDrops"/> is false.
    /// </summary>
    // State rather than enter/leave events: a missed leave would strand the
    // overlay.
    event Action<AssetDragState?>? AssetDragChanged;

    /// <summary>
    /// Whether this viewport is a drop target at all.
    /// </summary>
    // A native child gets input from the OS, not Avalonia, and has no
    // IDropTarget registered, so it cannot take a drop.
    bool AcceptsAssetDrops { get; }

    /// <summary>
    /// The running engine's host, once there is one. Setting it is what turns
    /// the viewport's input on; setting it to null is what turns it off.
    /// </summary>
    EngineHost? Host { get; set; }

    /// <summary>
    /// Whether this viewport still waits on the engine for a frame it asked
    /// for. A host whose engine has died clears <see cref="Host"/>, which
    /// stops the asking, and keeps the engine answering until this is false.
    /// </summary>
    // Always false for the native child: it asks the engine for nothing.
    bool IsAwaitingEngine { get; }

    /// <summary>
    /// Applies whatever cursor mode the engine has asked for. UI thread only,
    /// once per pass of the shell's pump.
    /// </summary>
    void PumpCursorMode();

    /// <summary>
    /// Ends this viewport for good. Called on the UI thread by the shell,
    /// before the control leaves the tree.
    /// </summary>
    // A detach alone is not a teardown: docking or floating the pane detaches
    // and re-attaches it, and ending the session there would lose the scene.
    // The native child does nothing here; destroying its HWND is the teardown.
    void Shutdown();

    /// <summary>Hands the keyboard to whatever the engine is listening through.</summary>
    // Native child: Win32 SetFocus on the HWND (Avalonia Focus() is a no-op).
    // Composited: plain Focus().
    void FocusEngine();

    /// <summary>
    /// This viewport as a control, for the shell to place and to anchor popups
    /// on. Returns <c>this</c>.
    /// </summary>
    Control Control { get; }
}

/// <summary>
/// Which viewport a session gets, and whether this machine can host one at all.
/// </summary>
// Both paths are Windows-only today: a Win32 child window, or a D3D11
// keyed-mutex NT handle plus ClipCursor.
public static class EngineViewports
{
    /// <summary>Whether this platform can host the engine in a viewport at all.</summary>
    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>Builds the viewport a session should run in.</summary>
    /// <param name="composited">
    /// Composite the engine's output instead of hosting a native child window.
    /// Pass the answer from <see cref="ViewportModePolicy.Decide"/>.
    /// </param>
    /// <param name="loggerFactory">Owned by the caller.</param>
    /// <param name="onUnavailable">
    /// Called on the UI thread if a composited viewport cannot be set up on
    /// this machine after all.
    /// </param>
    /// <param name="onFailure">
    /// Called on the UI thread when a running composited viewport stops
    /// working. Report it; do not swap viewports mid-session.
    /// </param>
    public static IEngineViewport Create(
        bool composited,
        ILoggerFactory loggerFactory,
        Action<string>? onUnavailable = null,
        Action<ViewportChoiceReason>? onFailure = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return composited
            ? new CompositionEngineViewport(
                loggerFactory.CreateLogger<CompositionEngineViewport>(), onUnavailable, onFailure)
            : new Win32EngineViewport();
    }
}
