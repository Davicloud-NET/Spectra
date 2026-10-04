using SpectraEngine.Core.Input;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Cameras;

/// <summary>
/// Default keyboard bindings for the viewport camera, by key name. A host
/// resolves its own key to a name and asks here.
/// </summary>
public static class EditorCameraShortcuts
{
    /// <summary>
    /// Resolves a key name and held modifiers to its default navigation verb.
    /// Case-insensitive. False when the key is not bound.
    /// </summary>
    public static bool TryResolve(string? keyName, KeyModifiers modifiers, out EditorCameraCommand command)
    {
        switch (keyName?.ToUpperInvariant())
        {
            case "F":
                command = (modifiers & KeyModifiers.Shift) != 0
                    ? EditorCameraCommand.FrameAll
                    : EditorCameraCommand.FrameSelection;
                return true;

            // Keypad, Blender's layout. The number row and letters are taken
            // by tools and inserts.
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

            case "KEYPAD5":
                command = EditorCameraCommand.ViewPerspective;
                return true;

            default:
                command = default;
                return false;
        }
    }

    /// <summary>Resolves a key name with no modifiers held.</summary>
    public static bool TryResolve(string? keyName, out EditorCameraCommand command) =>
        TryResolve(keyName, KeyModifiers.None, out command);

    /// <summary>
    /// The default bindings as display strings, for a keymap editor or a help
    /// overlay. Not valid input to <c>TryResolve</c>, which takes the bare key name.
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
