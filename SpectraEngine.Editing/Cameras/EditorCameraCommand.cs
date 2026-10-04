namespace SpectraEngine.Editing.Cameras;

/// <summary>
/// A discrete navigation verb for the viewport camera, handed to
/// <see cref="EditorCameraController.Apply"/>.
/// </summary>
public enum EditorCameraCommand
{
    /// <summary>Fit the current selection in view.</summary>
    FrameSelection,

    /// <summary>Fit every spatial node in the scene in view.</summary>
    FrameAll,

    // Set verbs, not a cycle: a toggle sent against a stale snapshot lands on
    // the wrong view.
    ViewPerspective,
    ViewTop,
    ViewBottom,
    ViewFront,
    ViewBack,
    ViewRight,
    ViewLeft,
}
