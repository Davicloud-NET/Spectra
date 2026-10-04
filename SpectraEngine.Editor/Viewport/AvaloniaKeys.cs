using Avalonia.Input;
using SpectraEngine.Core.Input;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;
using EngineKeyModifiers = SpectraEngine.Core.Input.KeyModifiers;

namespace SpectraEngine.Editor.Viewport;

// Avalonia input mapped to the engine's. Physical keys, US layout names.
// A key the engine has no name for is Unknown, never a nearby key.
internal static class AvaloniaKeys
{
    internal static InputKey ToInputKey(Key key)
    {
        // These rows are contiguous in both enums; a test checks it.
        if (key is >= Key.A and <= Key.Z)
            return InputKey.A + (key - Key.A);
        if (key is >= Key.D0 and <= Key.D9)
            return InputKey.Number0 + (key - Key.D0);
        if (key is >= Key.F1 and <= Key.F12)
            return InputKey.F1 + (key - Key.F1);
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return InputKey.Keypad0 + (key - Key.NumPad0);

        return key switch
        {
            Key.Escape => InputKey.Escape,
            Key.Return => InputKey.Enter,
            Key.Tab => InputKey.Tab,
            Key.Back => InputKey.Backspace,
            Key.Insert => InputKey.Insert,
            Key.Delete => InputKey.Delete,
            Key.Space => InputKey.Space,
            Key.Right => InputKey.Right,
            Key.Left => InputKey.Left,
            Key.Down => InputKey.Down,
            Key.Up => InputKey.Up,
            Key.PageUp => InputKey.PageUp,
            Key.PageDown => InputKey.PageDown,
            Key.Home => InputKey.Home,
            Key.End => InputKey.End,

            // Sides stay distinct: the fly camera binds the left ones.
            Key.LeftShift => InputKey.ShiftLeft,
            Key.RightShift => InputKey.ShiftRight,
            Key.LeftCtrl => InputKey.ControlLeft,
            Key.RightCtrl => InputKey.ControlRight,
            Key.LeftAlt => InputKey.AltLeft,
            Key.RightAlt => InputKey.AltRight,
            Key.LWin => InputKey.SuperLeft,
            Key.RWin => InputKey.SuperRight,
            Key.Apps => InputKey.Menu,

            Key.OemQuotes => InputKey.Apostrophe,
            Key.OemComma => InputKey.Comma,
            Key.OemMinus => InputKey.Minus,
            Key.OemPeriod => InputKey.Period,
            Key.OemQuestion => InputKey.Slash,
            Key.OemSemicolon => InputKey.Semicolon,
            Key.OemPlus => InputKey.Equal,
            Key.OemOpenBrackets => InputKey.LeftBracket,
            Key.OemPipe => InputKey.BackSlash,
            Key.OemCloseBrackets => InputKey.RightBracket,
            Key.OemTilde => InputKey.GraveAccent,

            Key.CapsLock => InputKey.CapsLock,
            Key.Scroll => InputKey.ScrollLock,
            Key.NumLock => InputKey.NumLock,
            Key.PrintScreen => InputKey.PrintScreen,
            Key.Pause => InputKey.Pause,

            _ => InputKey.Unknown,
        };
    }

    internal static EngineKeyModifiers ToModifiers(AvaloniaKeyModifiers modifiers)
    {
        EngineKeyModifiers result = EngineKeyModifiers.None;

        if ((modifiers & AvaloniaKeyModifiers.Shift) != 0)
            result |= EngineKeyModifiers.Shift;
        if ((modifiers & AvaloniaKeyModifiers.Control) != 0)
            result |= EngineKeyModifiers.Control;
        if ((modifiers & AvaloniaKeyModifiers.Alt) != 0)
            result |= EngineKeyModifiers.Alt;
        if ((modifiers & AvaloniaKeyModifiers.Meta) != 0)
            result |= EngineKeyModifiers.Super;

        return result;
    }

    // The button a press or release update is about. Extra buttons map to None.
    internal static PointerButtons ToPointerButton(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased =>
            PointerButtons.Left,
        PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased =>
            PointerButtons.Right,
        PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased =>
            PointerButtons.Middle,
        _ => PointerButtons.None,
    };

    // Nearest standard cursor. No rotate or grab in the standard set.
    internal static StandardCursorType ToStandardCursor(CursorShape shape) => shape switch
    {
        CursorShape.Crosshair => StandardCursorType.Cross,
        CursorShape.Grab => StandardCursorType.Hand,
        CursorShape.Grabbing => StandardCursorType.Hand,
        CursorShape.SizeWestEast => StandardCursorType.SizeWestEast,
        CursorShape.SizeNorthSouth => StandardCursorType.SizeNorthSouth,
        CursorShape.SizeNorthWestSouthEast => StandardCursorType.TopLeftCorner,
        CursorShape.SizeNorthEastSouthWest => StandardCursorType.TopRightCorner,
        CursorShape.SizeAll or CursorShape.Rotate => StandardCursorType.SizeAll,
        CursorShape.No => StandardCursorType.No,
        _ => StandardCursorType.Arrow,
    };
}
