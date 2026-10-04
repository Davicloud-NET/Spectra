namespace SpectraEngine.Editing.Gizmos;

/// <summary>Where a manipulator is in its interaction cycle.</summary>
public enum GizmoInteractionState
{
    /// <summary>Nothing selected, or the cursor is not over a handle.</summary>
    Idle,

    /// <summary>The cursor is over a handle. A press starts a drag.</summary>
    Hovering,

    /// <summary>A handle is held. An undo transaction is open.</summary>
    Dragging,
}

/// <summary>What one <see cref="GizmoTool.Update"/> call did.</summary>
public enum GizmoUpdateResult
{
    /// <summary>No selection, or the cursor is off the gizmo.</summary>
    None,

    /// <summary>The cursor is over a handle and no drag started this frame.</summary>
    Hovering,

    /// <summary>A drag started this frame.</summary>
    DragBegan,

    /// <summary>A drag is in progress.</summary>
    DragUpdated,

    /// <summary>
    /// The drag ended and landed in the undo history as one entry. A drag that
    /// ended where it started reports <see cref="DragCancelled"/> instead.
    /// </summary>
    DragCommitted,

    /// <summary>
    /// The drag ended and every node was restored. Nothing was recorded.
    /// </summary>
    DragCancelled,
}
