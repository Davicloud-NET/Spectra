using System;

namespace SpectraEngine.Editor.Shell.Logic;

// What a snapshot of the running level changed for the view.
[Flags]
internal enum LogicPlayChange
{
    None = 0,

    // The level said something new.
    Said = 1,

    // The level started or stopped.
    Playing = 2,

    // The tick's readout reads differently.
    Tick = 4,

    // The newest events are others.
    Events = 8,

    // A card's state line reads differently.
    States = 16,
}
