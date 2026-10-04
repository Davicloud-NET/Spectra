using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// How the engine drives an editor it cannot reference: one update and two
/// draws per frame, plus the state a host reports. Render thread only.
/// </summary>
// Core cannot reference SpectraEngine.Editing, so nothing here names an
// editing type. Every string is an interned constant: they are read on paths
// that must not allocate.
public interface ISceneEditor
{
    /// <summary>Advances the editor by one frame.</summary>
    /// <returns>
    /// True when the editor drove the viewport camera this frame, so the host
    /// should leave its own camera controller alone.
    /// </returns>
    bool Update(double deltaTime);

    /// <summary>
    /// Abandons any gesture in progress and gives up input capture, because the
    /// host is about to stop calling <see cref="Update"/>. Idempotent.
    /// </summary>
    // Needed, not just skipping Update: an open undo transaction or the
    // camera's cursor lock would otherwise never be released.
    void Suspend();

    /// <summary>
    /// The host is about to resume calling <see cref="Update"/>. Idempotent.
    /// While suspended the editor refuses edits.
    /// </summary>
    void Resume();

    /// <summary>
    /// Pushes the editor's overlay (handles, marquee) into this frame's debug
    /// line buffer. These draw on top of the scene, with no depth test.
    /// </summary>
    void Draw(DebugDraw output);

    /// <summary>
    /// Pushes world content such as the ground grid into this frame's
    /// depth-tested line buffer. These lines are tone-mapped, so colours are in
    /// linear light.
    /// </summary>
    void DrawWorld(DebugDraw output);

    /// <summary>How many nodes are selected.</summary>
    int SelectionCount { get; }

    /// <summary>The live manipulator: "move", "rotate" or "resize".</summary>
    string GizmoModeName { get; }

    /// <summary>The manipulator's handle style: "Studio" or "Classic".</summary>
    string GizmoStyleName { get; }

    /// <summary>The axis frame drags resolve against: "world" or "local".</summary>
    string GizmoOrientationName { get; }

    /// <summary>Whether the live manipulator quantises its drags.</summary>
    bool SnapEnabled { get; }

    /// <summary>
    /// The live manipulator's snap increment: world units for move and resize,
    /// degrees for rotate.
    /// </summary>
    float SnapIncrement { get; }

    /// <summary>The move tool's snap increment in world units, whichever tool is live.</summary>
    float MoveSnapIncrement { get; }

    /// <summary>The rotate tool's snap increment in degrees, whichever tool is live.</summary>
    float RotateSnapIncrement { get; }

    /// <summary>The resize tool's snap increment in world units, whichever tool is live.</summary>
    float ResizeSnapIncrement { get; }

    /// <summary>
    /// The navigation model driving the viewport camera ("freelook",
    /// "fly camera", …).
    /// </summary>
    string NavigationModeName { get; }

    /// <summary>
    /// When the ground grid shows: <c>"auto"</c> (during move and resize
    /// gestures), <c>"on"</c> or <c>"off"</c>.
    /// </summary>
    string GridModeName { get; }

    /// <summary>
    /// What the pointer would do right now, as one of a closed vocabulary:
    /// <c>suspended</c>, <c>fly</c>, <c>look</c>, <c>orbit</c>, <c>pan</c>,
    /// <c>drag-manipulate</c>, <c>drag-move</c>, <c>drag-box</c>,
    /// <c>hover-handle</c>, <c>hover-object</c>, <c>hover-empty</c>, <c>idle</c>.
    /// </summary>
    // Must come from the same arbitration that picks the cursor, or the two
    // disagree.
    string InteractionStateName { get; }

    /// <summary>
    /// Which view the editor camera is showing: "Perspective", "Top", "Bottom",
    /// "Front", "Back", "Right" or "Left".
    /// </summary>
    string ViewName { get; }

    /// <summary>
    /// True while a gesture is in flight: a manipulator drag, a marquee, or a
    /// value being scrubbed in a property panel. Camera navigation does not
    /// count. The host publishes snapshots faster while this is true
    /// (<see cref="Hosting.EngineHost.InteractiveSnapshotInterval"/>).
    /// </summary>
    bool IsInteracting { get; }

    /// <summary>How many edits can currently be undone.</summary>
    int UndoDepth { get; }

    /// <summary>How many undone edits can currently be redone.</summary>
    int RedoDepth { get; }
}
