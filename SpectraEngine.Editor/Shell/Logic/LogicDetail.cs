namespace SpectraEngine.Editor.Shell.Logic;

// How much of the graph is drawn, by how far out the view is.
internal enum LogicDetail
{
    // Everything.
    Full,

    // Cards with their headers. No port names, notes, state or labels.
    Compact,

    // Cards as plain boxes with a name.
    Far,

    // Cards as plain boxes. A name would be a smudge.
    Farthest,
}
