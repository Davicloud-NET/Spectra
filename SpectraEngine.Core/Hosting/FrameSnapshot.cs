using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Hosting;

/// <summary>
/// What a UI thread may know about a finished frame. Immutable values only,
/// no live objects, so it is safe to hold on any thread. It describes a frame
/// that is already over.
/// </summary>
public sealed class FrameSnapshot
{
    /// <summary>The empty snapshot, for a host that has not seen a frame yet.</summary>
    public static FrameSnapshot Empty { get; } = new();

    /// <summary>How many frames the engine has completed, counted from one.</summary>
    public long FrameNumber { get; init; }

    /// <summary>The smoothed frame time in milliseconds, as the engine reports it.</summary>
    public double FrameTimeMs { get; init; }

    /// <summary>The smoothed frame rate.</summary>
    public double Fps { get; init; }

    /// <summary>The ids of every currently selected node, in selection order.</summary>
    public IReadOnlyList<Guid> SelectedIds { get; init; } = Array.Empty<Guid>();

    /// <summary>
    /// The active tool, as the editor names it (<c>move</c>, <c>rotate</c>,
    /// <c>resize</c>), or null when the host installed no editor.
    /// </summary>
    public string? GizmoModeName { get; init; }

    /// <summary>The manipulator's handle style (<c>Studio</c>, <c>Classic</c>), or null.</summary>
    public string? GizmoStyleName { get; init; }

    /// <summary>The axis frame drags resolve against (<c>world</c>, <c>local</c>), or null.</summary>
    public string? GizmoOrientationName { get; init; }

    /// <summary>Whether the live manipulator quantises its drags.</summary>
    public bool SnapEnabled { get; init; }

    /// <summary>
    /// The live manipulator's snap increment, in world units for move and
    /// resize and in degrees for rotate. Which unit applies is decided by
    /// <see cref="GizmoModeName"/>, so a UI showing one must show the other.
    /// </summary>
    public float SnapIncrement { get; init; }

    /// <summary>The move tool's snap increment in world units, whichever tool is live.</summary>
    public float MoveSnapIncrement { get; init; }

    /// <summary>The rotate tool's snap increment in degrees, whichever tool is live.</summary>
    public float RotateSnapIncrement { get; init; }

    /// <summary>The resize tool's snap increment in world units, whichever tool is live.</summary>
    public float ResizeSnapIncrement { get; init; }

    /// <summary>Which camera is driving, as the editor names it, or null when there is no editor.</summary>
    public string? NavigationModeName { get; init; }

    /// <summary>The viewport camera's world position this frame.</summary>
    public System.Numerics.Vector3 CameraPosition { get; init; }

    /// <summary>
    /// When the ground grid shows: "auto" (during move and resize gestures),
    /// "on" or "off". Null when there is no editor.
    /// </summary>
    public string? GridModeName { get; init; }

    /// <summary>
    /// Whether play mode is active: the character has the camera and the cursor,
    /// and the editor is suspended.
    /// </summary>
    public bool IsPlaying { get; init; }

    /// <summary>
    /// Whether play mode can be entered: the engine built a character over the
    /// active scene. False until the scene has loaded.
    /// </summary>
    public bool CanPlay { get; init; }

    /// <summary>The debug visualisations currently drawn over the scene.</summary>
    public DebugVisualization DebugFlags { get; init; }

    /// <summary>The rendering pipeline currently drawing the scene, or null before the renderer reported one.</summary>
    public string? PipelineName { get; init; }

    /// <summary>
    /// Every pipeline the running backend registered, in registration order.
    /// The same list instance rides every snapshot.
    /// </summary>
    public IReadOnlyList<string> PipelineNames { get; init; } = Array.Empty<string>();

    /// <summary>How many edits can be undone.</summary>
    public int UndoDepth { get; init; }

    /// <summary>How many undone edits can be redone.</summary>
    public int RedoDepth { get; init; }

    /// <summary>How many static-world compiles have landed for the active scene.</summary>
    public int StaticWorldCompileCount { get; init; }

    /// <summary>
    /// Why the static world stopped recompiling, or null when it is current.
    /// A shell should show this: the viewport keeps drawing the last good world.
    /// </summary>
    public string? StaticWorldDefect { get; init; }

    /// <summary>
    /// How many errors a graphics validation layer has reported over the
    /// renderer's life. Zero when no layer is running, so read it with
    /// <see cref="DebugLayerActive"/>.
    /// </summary>
    public int DebugLayerErrorCount { get; init; }

    /// <summary>
    /// Whether the validation layer producing
    /// <see cref="DebugLayerErrorCount"/> is running.
    /// </summary>
    public bool DebugLayerActive { get; init; }

    /// <summary>
    /// How many cached asset references failed and are bound to the placeholder:
    /// a texture that would not decode, a material file that would not read.
    /// A decode still in flight is not counted.
    /// </summary>
    public int PlaceholderBoundCount { get; init; }

    /// <summary>
    /// What the pointer would do right now. See
    /// <see cref="Scene.ISceneEditor.InteractionStateName"/> for the vocabulary.
    /// </summary>
    public string? InteractionStateName { get; init; }

    /// <summary>
    /// Which view the editor camera is showing. See
    /// <see cref="Scene.ISceneEditor.ViewName"/> for the vocabulary.
    /// </summary>
    public string? ViewName { get; init; }

    /// <summary>
    /// Mean time the render thread spent waiting for the shared target's key
    /// since the last snapshot, in milliseconds. Zero on a windowed surface.
    /// Tells a slow producer from a stalled consumer, which frame time cannot.
    /// </summary>
    public float SharedAcquireWaitMs { get; init; }

    /// <summary>
    /// The longest single wait for the shared target's key since the last
    /// snapshot, in milliseconds.
    /// </summary>
    public float SharedAcquirePeakMs { get; init; }

    /// <summary>
    /// The selection's editable properties, merged across every selected node.
    /// </summary>
    public IReadOnlyList<PropertyRow> SelectionProperties { get; init; } = Array.Empty<PropertyRow>();

    /// <summary>
    /// The selected entity's class, declared outputs and wiring, or null when
    /// the selection is not exactly one node carrying an entity.
    /// </summary>
    public EntityPanelInfo? SelectionEntity { get; init; }

    /// <summary>
    /// Every entity's wires, while a host asked for them with
    /// <see cref="EngineHost.RequestLogicView"/>, otherwise null. The same
    /// instance rides every snapshot until the wiring changes.
    /// </summary>
    public LogicGraphInfo? LogicGraph { get; init; }

    /// <summary>
    /// The shared colour target a composited host should import and present, or
    /// null when the engine presents for itself. Re-import when the generation
    /// changes; handle values are recycled and do not identify a target.
    /// </summary>
    public Renderer.SharedTargetHandle? SharedTarget { get; init; }

    /// <summary>
    /// The structural changes since the previous snapshot, in the order they
    /// happened. Usually empty.
    /// </summary>
    public IReadOnlyList<SceneChange> Changes { get; init; } = Array.Empty<SceneChange>();

    /// <summary>
    /// True when more changes happened than the log could hold, so
    /// <see cref="Changes"/> is incomplete and a view must rebuild rather than
    /// replay. Also set for the first snapshot after a scene swap.
    /// </summary>
    public bool ChangesOverflowed { get; init; }

    /// <summary>
    /// What the console printed since the previous snapshot, in order. Sent
    /// once, so a reader that skips a snapshot loses its lines. A gap in
    /// <see cref="ConsoleLine.Sequence"/> says how many.
    /// </summary>
    public IReadOnlyList<ConsoleLine> ConsoleLines { get; init; } = Array.Empty<ConsoleLine>();
}
