namespace SpectraEngine.Core.Input;

/// <summary>
/// Physical keys, named by what is printed on a US layout, independent of any
/// windowing backend. Not characters: text entry is a separate problem.
/// </summary>
// Names match Silk.NET's spelling so the translation table can be checked by
// name in a test.
public enum InputKey
{
    /// <summary>A key the source could not name. Never matches a binding.</summary>
    Unknown = 0,

    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    // The number row, not the keypad.
    Number0, Number1, Number2, Number3, Number4,
    Number5, Number6, Number7, Number8, Number9,

    // Separate from the number row: the editor binds views to the keypad and
    // tools to the number row.
    Keypad0, Keypad1, Keypad2, Keypad3, Keypad4,
    Keypad5, Keypad6, Keypad7, Keypad8, Keypad9,

    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    Escape,
    Enter,
    Tab,
    Backspace,
    Insert,
    Delete,
    Space,
    Right,
    Left,
    Down,
    Up,
    PageUp,
    PageDown,
    Home,
    End,

    // Left and right stay distinct here; KeyModifiers collapses them for chords.
    ShiftLeft, ShiftRight,
    ControlLeft, ControlRight,
    AltLeft, AltRight,
    SuperLeft, SuperRight,

    Apostrophe,
    Comma,
    Minus,
    Period,
    Slash,
    Semicolon,
    Equal,
    LeftBracket,
    BackSlash,
    RightBracket,
    GraveAccent,

    CapsLock,
    ScrollLock,
    NumLock,
    PrintScreen,
    Pause,
    Menu,
}
