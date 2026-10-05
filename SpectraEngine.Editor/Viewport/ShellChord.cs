namespace SpectraEngine.Editor.Viewport;

/// <summary>
/// A keyboard chord that belongs to the shell rather than to the engine.
/// </summary>
// A focused native child viewport gets the keyboard from the OS and Avalonia's
// accelerators never fire, so the viewport intercepts these and raises them.
// Only verbs the engine keymap does not own go here.
public enum ShellChord
{
    /// <summary>
    /// F11: give the viewport the whole window, or put the panels back.
    /// </summary>
    MaximiseViewport,

    /// <summary>Ctrl+backtick: show or hide the bottom region.</summary>
    ToggleBottomDrawer,

    /// <summary>
    /// Backtick: show the console and put the caret in it. The one chord
    /// raised while the cursor is held, so it works while playing.
    /// </summary>
    ShowConsole,

    /// <summary>Ctrl+N.</summary>
    NewMap,

    /// <summary>Ctrl+O.</summary>
    OpenMap,

    /// <summary>Ctrl+S.</summary>
    SaveMap,

    /// <summary>Ctrl+Shift+S.</summary>
    SaveMapAs,

    /// <summary>Ctrl+1: a block, at the centre of the view.</summary>
    InsertBlock,

    /// <summary>Ctrl+2: a part.</summary>
    InsertPart,

    /// <summary>Ctrl+3: a cut.</summary>
    InsertCut,

    /// <summary>Ctrl+4: a light.</summary>
    InsertLight,

    /// <summary>Ctrl+P: the command palette.</summary>
    OpenPalette,
}
