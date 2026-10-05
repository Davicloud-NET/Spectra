using System;

namespace SpectraEngine.Editor.Shell.Logic;

// What a refresh of a LogicArrangement rebuilt.
[Flags]
internal enum LogicArrangementChange
{
    None = 0,

    // The part of the graph on show is another: other cards, or other cards dimmed.
    Shown = 1,

    // The scene was laid out again.
    Scene = 2,

    // A drawing would differ.
    Looks = 4,
}
