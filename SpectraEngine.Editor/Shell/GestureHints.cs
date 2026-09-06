namespace SpectraEngine.Editor.Shell;

/// <summary>
/// What the mouse and the modifiers do right now, in words.
/// </summary>
/// <remarks>
/// <para>
/// <b>Continuous contextual disclosure is the only teaching mechanism that
/// reaches people who will never read documentation</b>, which is nearly
/// everybody: they start clicking immediately and learn by doing. This editor
/// had none, and spent the space on five engine counters instead.
/// </para>
/// <para>
/// <b>A hand-written table returning literals.</b> No formatting, no
/// concatenation, so reading it once per snapshot allocates nothing; and the
/// state it switches on comes from the editor, so the hint cannot advertise a
/// gesture the next click will not perform.
/// </para>
/// </remarks>
public static class GestureHints
{
    /// <summary>What to show for one interaction state.</summary>
    /// <param name="interactionState">
    /// The editor's own word for what the pointer would do. An unknown one reads
    /// as idle rather than as nothing: a blank status bar looks broken.
    /// </param>
    /// <param name="gizmoMode">The live tool, for naming what a drag will do.</param>
    /// <param name="snapEnabled">Whether Alt turns snapping on or off.</param>
    public static string For(string? interactionState, string gizmoMode, bool snapEnabled) =>
        interactionState switch
        {
            // Play mode has its own standing chip, which says more than this
            // could and says it in the standing-state area.
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
