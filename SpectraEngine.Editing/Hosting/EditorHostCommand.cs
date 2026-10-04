namespace SpectraEngine.Editing.Hosting;

/// <summary>
/// The editor verbs that belong to the host rather than to a manipulator or a
/// camera: history, structural edits, selection and mode switches. Applied
/// through <see cref="SceneEditorHost.Apply(EditorHostCommand)"/> on the render
/// thread; a UI thread reaches it through <c>EngineHost.EnqueueCommand</c>.
/// </summary>
public enum EditorHostCommand
{
    /// <summary>Steps one entry back through the undo history.</summary>
    Undo,

    /// <summary>Steps one entry forward, if nothing has invalidated the redo stack.</summary>
    Redo,

    /// <summary>Copies the selection's roots and selects the copies.</summary>
    Duplicate,

    /// <summary>Removes the selection's roots.</summary>
    Delete,

    /// <summary>Puts the selection's roots under one new parent node.</summary>
    Group,

    /// <summary>Dissolves the selected groups, keeping their children in place.</summary>
    Ungroup,

    /// <summary>
    /// Converts the selected brushes between world geometry and parts. A mixed
    /// selection normalises rather than flipping node by node.
    /// </summary>
    ToggleBrushKind,

    /// <summary>
    /// Swaps between the editor's own freelook camera and the engine's fly
    /// camera.
    /// </summary>
    ToggleNavigation,

    /// <summary>
    /// Selects the root's direct children. Top-level nodes only: moving them
    /// moves everything, and the property panel stays proportional to the tree.
    /// </summary>
    SelectAll,

    /// <summary>Empties the selection.</summary>
    ClearSelection,

    /// <summary>Ground grid shows during move and resize gestures only. The default.</summary>
    // Three set verbs, not a cycle: a cycle sent against a stale snapshot
    // lands on the wrong mode.
    GridAuto,

    /// <summary>Ground grid always drawn.</summary>
    GridOn,

    /// <summary>Ground grid never drawn.</summary>
    GridOff,
}
