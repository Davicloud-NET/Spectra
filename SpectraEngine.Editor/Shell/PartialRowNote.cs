using SpectraEngine.Core.Inspection;
using SpectraEngine.Editing.Commands;

namespace SpectraEngine.Editor.Shell;

// What an attribute's row says under itself when only some of the selected
// nodes have the attribute: how many do, and where a new value goes. The
// editor leaves out a node that cannot take one more, so the row says which
// and why before anything is typed.
internal static class PartialRowNote
{
    public static string Of(in AttributePanelRow row, int selectionCount)
    {
        if (row.Count >= selectionCount)
            return string.Empty;

        string has = $"On {row.Count} of {selectionCount}.";
        if (row.LeftOutCount == 0)
            return $"{has} A new value goes on all {selectionCount}.";

        int reach = selectionCount - row.LeftOutCount;
        string goes = reach > row.Count
            ? $"A new value goes on {reach} of them."
            : reach == 1 ? "A new value stays on that one." : $"A new value stays on those {reach}.";

        return $"{has} {goes} {PropertyLimits.LeftOut(row.LeftOut, row.Name)}";
    }
}
