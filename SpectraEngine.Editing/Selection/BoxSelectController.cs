using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Input;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Selection;

/// <summary>
/// The marquee: tracks a rectangle from press to release, draws it, and on
/// release applies everything it covered to the scene's selection in one
/// change event. The caller decides when a press starts one.
/// </summary>
// Render thread only. The outline is a world-space quad just past the near
// plane, drawn as depth-off debug lines, so it needs no 2D pipeline.
public sealed class BoxSelectController
{
    // Fraction of the near distance. Past depth rounding, still in front of everything.
    private const float OverlayNearOffset = 0.1f;

    private readonly List<SceneNode> _results = [];

    private Vector2 _anchor;
    private Vector2 _current;

    /// <summary>Creates a marquee over a scene.</summary>
    public BoxSelectController(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Scene = scene;
    }

    /// <summary>The scene whose nodes the marquee selects.</summary>
    public Scene Scene { get; }

    /// <summary>Whether a node must touch the rectangle or lie fully inside it.</summary>
    public BoxSelectMode Mode { get; set; } = BoxSelectMode.Intersect;

    /// <summary>The button whose release commits the marquee.</summary>
    public PointerButtons DragButton { get; set; } = PointerButtons.Left;

    /// <summary>
    /// How long the rectangle's longer side must get before the gesture counts
    /// as a drag rather than a click.
    /// </summary>
    public float ClickThresholdPixels { get; set; } = 3f;

    /// <summary>The colour the marquee outline is drawn in.</summary>
    public Vector3 OutlineColor { get; set; } = new(1f, 0.75f, 0.15f);

    /// <summary>True while a marquee is being dragged.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The current rectangle. Only valid while <see cref="IsActive"/>.</summary>
    public ScreenRect Rect => ScreenRect.FromCorners(_anchor, _current);

    /// <summary>True when releasing now would resolve as a click.</summary>
    public bool IsClick => Rect.LongestSide < ClickThresholdPixels;

    /// <summary>
    /// The nodes the last committed marquee covered. Empty after a cancel or a click.
    /// </summary>
    public IReadOnlyList<SceneNode> LastResult => _results;

    /// <summary>Raised after a marquee committed, carrying how many nodes it covered.</summary>
    public event Action<int>? Committed;

    /// <summary>
    /// Starts a marquee anchored at <paramref name="cursorPosition"/>. One
    /// already in progress is restarted, so a lost release edge cannot strand it.
    /// </summary>
    public void Begin(Vector2 cursorPosition)
    {
        _anchor = cursorPosition;
        _current = cursorPosition;
        _results.Clear();
        IsActive = true;
    }

    /// <summary>
    /// Advances the marquee by one frame: tracks the cursor, and on the release
    /// edge (or <paramref name="cancelRequested"/>) ends the gesture.
    /// </summary>
    public BoxSelectResult Update(in EditorInputFrame frame, bool cancelRequested = false)
    {
        if (!IsActive)
            return BoxSelectResult.None;

        if (cancelRequested)
        {
            Cancel();
            return BoxSelectResult.Cancelled;
        }

        // Track before testing the release, so a same-frame press and release
        // still sees where the cursor ended up.
        _current = frame.CursorPosition;

        if (!frame.WasReleased(DragButton))
            return BoxSelectResult.Dragging;

        return Commit(frame.Modifiers, frame.ViewportSize);
    }

    /// <summary>
    /// Abandons the marquee without touching the selection. False when none
    /// was in progress.
    /// </summary>
    public bool Cancel()
    {
        if (!IsActive)
            return false;

        IsActive = false;
        _results.Clear();
        return true;
    }

    /// <summary>
    /// Ends the gesture and applies it to the selection: a drag selects what it
    /// covered, a click on empty space clears the selection unless a modifier
    /// is held.
    /// </summary>
    public BoxSelectResult Commit(KeyModifiers modifiers, Vector2 viewportSize)
    {
        if (!IsActive)
            return BoxSelectResult.None;

        IsActive = false;
        SelectionUpdate update = SelectionModifiers.Resolve(modifiers);

        if (Rect.LongestSide < ClickThresholdPixels)
        {
            _results.Clear();
            // A modified click on nothing is a miss; keep the selection.
            if (update == SelectionUpdate.Replace)
                Scene.Selection.Clear();

            Committed?.Invoke(0);
            return BoxSelectResult.Clicked;
        }

        ScreenRect rect = Rect;
        BoxSelectQuery.Query(Scene, in rect, viewportSize, Mode, _results);

        Scene.Selection.Apply(_results, update);

        Committed?.Invoke(_results.Count);
        return BoxSelectResult.Committed;
    }

    /// <summary>
    /// Draws the marquee outline. Nothing is drawn while the gesture is still
    /// within the click threshold.
    /// </summary>
    public void Draw(DebugDraw output, Vector2 viewportSize)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!IsActive || IsClick || viewportSize.X <= 0f || viewportSize.Y <= 0f)
            return;

        ScreenRect rect = Rect;
        Camera camera = Scene.Camera;
        float offset = camera.NearPlane * OverlayNearOffset;

        Vector3 topLeft = CornerToWorld(camera, new Vector2(rect.Min.X, rect.Min.Y), viewportSize, offset);
        Vector3 topRight = CornerToWorld(camera, new Vector2(rect.Max.X, rect.Min.Y), viewportSize, offset);
        Vector3 bottomRight = CornerToWorld(camera, new Vector2(rect.Max.X, rect.Max.Y), viewportSize, offset);
        Vector3 bottomLeft = CornerToWorld(camera, new Vector2(rect.Min.X, rect.Max.Y), viewportSize, offset);

        output.Line(topLeft, topRight, OutlineColor);
        output.Line(topRight, bottomRight, OutlineColor);
        output.Line(bottomRight, bottomLeft, OutlineColor);
        output.Line(bottomLeft, topLeft, OutlineColor);
    }

    // The ray starts on the near plane; step along it so the quad is not clipped.
    private static Vector3 CornerToWorld(Camera camera, Vector2 pixel, Vector2 viewportSize, float offset) =>
        camera.ScreenPointToRay(pixel, viewportSize).PointAt(offset);
}

/// <summary>What one <see cref="BoxSelectController.Update"/> call did.</summary>
public enum BoxSelectResult
{
    /// <summary>No marquee is in progress.</summary>
    None,

    /// <summary>The marquee is being dragged; the selection has not changed.</summary>
    Dragging,

    /// <summary>The marquee ended as a drag and its coverage landed in the selection.</summary>
    Committed,

    /// <summary>The marquee never moved far enough to be a drag and resolved as a click on empty space.</summary>
    Clicked,

    /// <summary>The marquee was abandoned; the selection is untouched.</summary>
    Cancelled,
}
