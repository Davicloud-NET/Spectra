using SpectraEngine.Core.Input;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Cameras;

/// <summary>
/// The recommended default keyboard bindings for the viewport camera, expressed
/// as key <em>names</em> — the same scheme, and the same reasoning, as
/// <see cref="Gizmos.GizmoShortcuts"/>: a host resolves its own key to a name,
/// asks here, and calls <see cref="EditorCameraController.Apply"/> with what
/// comes back.
/// </summary>
/// <remarks>
/// <b>THE BINDINGS.</b> <b>F</b> frames the selection — Blender, Maya, Unity,
/// Unreal and Godot all agree, which makes it about as close to a universal
/// binding as 3D editing has. <b>Shift+F</b> frames the whole scene, following
/// the same "widen the same verb" convention Unity uses; the modifier is passed
/// separately rather than baked into a key name, because modifier state already
/// travels with the input frame.
/// <para>
/// The table is a default, not a policy: a host with its own keymap should read
/// its own bindings and use this only as a fallback. Matching is ordinal and
/// case-insensitive, and the table is a plain switch — nothing reflects or
/// allocates.
/// </para>
/// </remarks>
public static class EditorCameraShortcuts
{
    /// <summary>
    /// Resolves a key name plus the modifiers held with it to the navigation
    /// verb it is bound to by default, or returns false when the key is not
    /// bound.
    /// </summary>
    /// <param name="keyName">The key's name as the host's input stack spells it — <c>"F"</c>.</param>
    /// <param name="modifiers">Modifiers held at the time of the press.</param>
    /// <param name="command">The verb the key is bound to, when this returns true.</param>
    public static bool TryResolve(string? keyName, KeyModifiers modifiers, out EditorCameraCommand command)
    {
        switch (keyName?.ToUpperInvariant())
        {
            case "F":
                command = (modifiers & KeyModifiers.Shift) != 0
                    ? EditorCameraCommand.FrameAll
                    : EditorCameraCommand.FrameSelection;
                return true;

            // The KEYPAD, and the reason is what is left over: the number row's
            // 2, 3 and 4 are the Studio tool row and Ctrl+1 to Ctrl+4 insert,
            // while the letters are tools or are claimed by a driving camera.
            // Blender's numpad layout is also the one every user arriving from
            // another editor already has in their hands.
            case "KEYPAD7":
                command = (modifiers & KeyModifiers.Control) != 0
                    ? EditorCameraCommand.ViewBottom
                    : EditorCameraCommand.ViewTop;
                return true;

            case "KEYPAD1":
                command = (modifiers & KeyModifiers.Control) != 0
                    ? EditorCameraCommand.ViewBack
                    : EditorCameraCommand.ViewFront;
                return true;

            case "KEYPAD3":
                command = (modifiers & KeyModifiers.Control) != 0
                    ? EditorCameraCommand.ViewLeft
                    : EditorCameraCommand.ViewRight;
                return true;

            // A SET verb rather than a toggle, for the reason every other verb
            // this shell posts is one: a control displays which view is live.
            case "KEYPAD5":
                command = EditorCameraCommand.ViewPerspective;
                return true;

            default:
                command = default;
                return false;
        }
    }

    /// <summary>
    /// Resolves a key name with no modifiers held. The common case, and the
    /// overload a host without a modifier-aware keymap can call.
    /// </summary>
    public static bool TryResolve(string? keyName, out EditorCameraCommand command) =>
        TryResolve(keyName, KeyModifiers.None, out command);

    /// <summary>
    /// The default bindings as data, for a keymap editor or a help overlay.
    /// These are <em>display</em> strings — the modifier prefix is spelled out
    /// for a human reader, whereas
    /// <see cref="TryResolve(string?, KeyModifiers, out EditorCameraCommand)"/>
    /// takes the bare key name and the modifier set separately.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, EditorCameraCommand>> Defaults { get; } =
    [
        new("F", EditorCameraCommand.FrameSelection),
        new("Shift+F", EditorCameraCommand.FrameAll),
        new("Keypad5", EditorCameraCommand.ViewPerspective),
        new("Keypad7", EditorCameraCommand.ViewTop),
        new("Ctrl+Keypad7", EditorCameraCommand.ViewBottom),
        new("Keypad1", EditorCameraCommand.ViewFront),
        new("Ctrl+Keypad1", EditorCameraCommand.ViewBack),
        new("Keypad3", EditorCameraCommand.ViewRight),
        new("Ctrl+Keypad3", EditorCameraCommand.ViewLeft),
    ];
}
