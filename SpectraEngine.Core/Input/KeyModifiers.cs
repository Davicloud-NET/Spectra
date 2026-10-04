using System;

namespace SpectraEngine.Core.Input;

/// <summary>
/// Modifier keys held, independent of the windowing backend. Left and right
/// physical keys collapse into one flag.
/// </summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>No modifiers held.</summary>
    None = 0,

    /// <summary>Either Shift key is held.</summary>
    Shift = 1 << 0,

    /// <summary>Either Control key is held.</summary>
    Control = 1 << 1,

    /// <summary>Either Alt key is held.</summary>
    Alt = 1 << 2,

    /// <summary>Either Super/Windows/Command key is held.</summary>
    Super = 1 << 3,
}
