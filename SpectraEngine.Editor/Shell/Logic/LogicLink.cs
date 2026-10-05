namespace SpectraEngine.Editor.Shell.Logic;

// One hop of a wire between two neighbouring columns, seen from one of its
// ends. The offsets are how far under each node's top the wire meets it.
internal readonly record struct LogicLink(LogicLayoutNode Other, double OtherOffset, double OwnOffset);
