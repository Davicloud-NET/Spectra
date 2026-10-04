using System;

namespace SpectraEngine.Core.Input;

/// <summary>
/// Mouse buttons, independent of the windowing backend. Only the three the
/// engine binds.
/// </summary>
[Flags]
public enum PointerButtons
{
    /// <summary>No buttons.</summary>
    None = 0,

    /// <summary>The primary (left) button.</summary>
    Left = 1 << 0,

    /// <summary>The secondary (right) button.</summary>
    Right = 1 << 1,

    /// <summary>The middle (wheel) button.</summary>
    Middle = 1 << 2,
}
