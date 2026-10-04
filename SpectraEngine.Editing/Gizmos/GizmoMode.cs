namespace SpectraEngine.Editing.Gizmos;

/// <summary>Which manipulator the viewport is offering.</summary>
public enum GizmoMode
{
    /// <summary>The move tool (<see cref="TranslateGizmo"/>).</summary>
    Translate,

    /// <summary>The rotate tool (<see cref="RotateGizmo"/>).</summary>
    Rotate,

    /// <summary>The resize tool (<see cref="ScaleGizmo"/>).</summary>
    Scale,
}

/// <summary>
/// Which frame a gizmo lays its handles out in. Translate and rotate honour
/// both; <see cref="ScaleGizmo"/> is always local.
/// </summary>
public enum GizmoOrientation
{
    /// <summary>Handles lie along the world axes. The default.</summary>
    World,

    /// <summary>Handles lie along the last-selected node's own axes.</summary>
    Local,
}

/// <summary>
/// A verb for <see cref="GizmoController.Apply"/>. The host owns the keymap and
/// resolves a key, button or menu item to one of these.
/// </summary>
public enum GizmoCommand
{
    /// <summary>Switch to the move tool.</summary>
    UseTranslate,

    /// <summary>Switch to the rotate tool.</summary>
    UseRotate,

    /// <summary>Switch to the resize tool.</summary>
    UseScale,

    /// <summary>Advance to the next mode, wrapping.</summary>
    CycleMode,

    /// <summary>Flip between world- and local-aligned handles.</summary>
    ToggleOrientation,

    /// <summary>Flip between the Studio and Classic manipulator styles.</summary>
    ToggleStyle,

    /// <summary>Turn snapping on or off.</summary>
    ToggleSnap,

    // Set verbs for UI controls. A toggle sent against a stale snapshot flips
    // the wrong way; keyboard chords keep the toggles.

    /// <summary>Lay the handles along the world axes. Idempotent.</summary>
    UseWorldOrientation,

    /// <summary>Lay the handles along the reference node's own axes. Idempotent.</summary>
    UseLocalOrientation,

    /// <summary>Use the Studio manipulator style. Idempotent.</summary>
    UseStudioStyle,

    /// <summary>Use the Classic manipulator style. Idempotent.</summary>
    UseClassicStyle,

    /// <summary>Turn snapping on. Idempotent.</summary>
    EnableSnap,

    /// <summary>Turn snapping off. Idempotent.</summary>
    DisableSnap,

    /// <summary>Step every snap increment one rung finer.</summary>
    FinerSnap,

    /// <summary>Step every snap increment one rung coarser.</summary>
    CoarserSnap,

    /// <summary>Abort an in-progress drag, restoring the selection.</summary>
    Cancel,
}
