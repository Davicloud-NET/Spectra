namespace SpectraEngine.Editor.Shell.Logic;

// The shape a wire is drawn in.
internal enum LogicWireRoute
{
    // Left to right, to a later column.
    Forward,

    // To an earlier column: down, along under the group, and up.
    Back,

    // To the card it leaves: under the card and back in.
    Loop,
}
