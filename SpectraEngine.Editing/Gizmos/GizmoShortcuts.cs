using System;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// Default gizmo key bindings, by key name. Names keep this assembly free of
/// any windowing backend's key enum.
/// </summary>
// 2/3/4 is the Roblox Studio row (move, resize, rotate) and W/E/R the
// Unity/Unreal row (move, rotate, resize). The two orders differ on purpose.
public static class GizmoShortcuts
{
    /// <summary>
    /// Resolves a key name ("W", "Number2", "Escape") to its default verb.
    /// Case-insensitive. False when the key is not bound.
    /// </summary>
    public static bool TryResolve(string? keyName, out GizmoCommand command)
    {
        switch (keyName?.ToUpperInvariant())
        {
            // Hosts spell the digit row differently: Number2, D2, Keypad2, 2.
            case "2" or "NUMBER2" or "D2" or "KEYPAD2":
                command = GizmoCommand.UseTranslate;
                return true;
            case "3" or "NUMBER3" or "D3" or "KEYPAD3":
                command = GizmoCommand.UseScale;
                return true;
            case "4" or "NUMBER4" or "D4" or "KEYPAD4":
                command = GizmoCommand.UseRotate;
                return true;

            case "W":
                command = GizmoCommand.UseTranslate;
                return true;
            case "E":
                command = GizmoCommand.UseRotate;
                return true;
            case "R":
                command = GizmoCommand.UseScale;
                return true;

            case "X":
                command = GizmoCommand.ToggleOrientation;
                return true;

            case "Y":
                command = GizmoCommand.ToggleStyle;
                return true;

            case "G":
                command = GizmoCommand.ToggleSnap;
                return true;
            case "[" or "LEFTBRACKET" or "OEM4":
                command = GizmoCommand.FinerSnap;
                return true;
            case "]" or "RIGHTBRACKET" or "OEM6":
                command = GizmoCommand.CoarserSnap;
                return true;

            case "ESCAPE" or "ESC":
                command = GizmoCommand.Cancel;
                return true;

            default:
                command = default;
                return false;
        }
    }

    /// <summary>The default bindings as data, for a keymap editor or help overlay.</summary>
    public static IReadOnlyList<KeyValuePair<string, GizmoCommand>> Defaults { get; } =
    [
        new("2", GizmoCommand.UseTranslate),
        new("3", GizmoCommand.UseScale),
        new("4", GizmoCommand.UseRotate),
        new("W", GizmoCommand.UseTranslate),
        new("E", GizmoCommand.UseRotate),
        new("R", GizmoCommand.UseScale),
        new("X", GizmoCommand.ToggleOrientation),
        new("Y", GizmoCommand.ToggleStyle),
        new("G", GizmoCommand.ToggleSnap),
        new("[", GizmoCommand.FinerSnap),
        new("]", GizmoCommand.CoarserSnap),
        new("Escape", GizmoCommand.Cancel),
    ];
}
