namespace SpectraEngine.Editor.Shell;

/// <summary>
/// What the mouse and the modifiers do right now, in words, for the status bar.
/// </summary>
// Literals only: read once per snapshot, so it must not allocate.
public static class GestureHints
{
    /// <summary>The hint for one interaction state. An unknown state reads as idle.</summary>
    /// <param name="interactionState">The editor's word for what the pointer would do.</param>
    /// <param name="snapEnabled">Whether snapping is on, which decides what Alt does.</param>
    public static string For(string? interactionState, string gizmoMode, bool snapEnabled) =>
        interactionState switch
        {
            // Play mode has its own chip.
            "suspended" => "",

            "look" => "look  ·  W A S D fly  ·  Q E down and up  ·  wheel trims speed",
            "orbit" => "orbit  ·  release to stop",
            "pan" => "pan",
            "fly" => "fly camera  ·  W A S D  ·  right-drag looks  ·  F7 for the editor camera",

            "drag-manipulate" or "drag-move" => DragHint(gizmoMode, snapEnabled),
            "drag-box" => "box select  ·  Ctrl adds  ·  Esc cancels",

            "hover-handle" => HoverHandleHint(gizmoMode),
            "hover-object" => "click selects  ·  Ctrl adds  ·  drag moves  ·  Alt+drag orbits  ·  right-drag looks",
            "hover-empty" => "drag box-selects  ·  Alt+drag orbits  ·  right-drag looks  ·  wheel zooms",

            _ => "Alt+drag orbits  ·  right-drag looks  ·  F frames the selection",
        };

    private static string DragHint(string gizmoMode, bool snapEnabled) => gizmoMode switch
    {
        "rotate" => snapEnabled
            ? "rotate  ·  Alt drags freely  ·  Esc cancels"
            : "rotate  ·  Alt snaps  ·  Esc cancels",

        "resize" => snapEnabled
            ? "resize  ·  Alt drags freely  ·  Shift anchors the far side  ·  Esc cancels"
            : "resize  ·  Alt snaps  ·  Shift anchors the far side  ·  Esc cancels",

        _ => snapEnabled
            ? "move  ·  Alt drags freely  ·  Esc cancels"
            : "move  ·  Alt snaps  ·  Esc cancels",
    };

    private static string HoverHandleHint(string gizmoMode) => gizmoMode switch
    {
        "rotate" => "drag to rotate  ·  Alt+drag orbits  ·  right-drag looks",
        "resize" => "drag to resize  ·  Alt+drag orbits  ·  right-drag looks",
        _ => "drag to move  ·  Alt+drag orbits  ·  right-drag looks",
    };
}
