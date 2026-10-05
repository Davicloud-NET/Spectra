using System;

namespace SpectraEngine.Editor.Shell.Logic;

// The order the layout takes cards in: by name, then by what makes two cards
// of one name different. It does not depend on the order the scene lists its
// entities in, so neither does the picture.
internal static class LogicCardOrder
{
    public static int Compare(LogicCard a, LogicCard b)
    {
        int byName = string.CompareOrdinal(a.Name, b.Name);
        if (byName != 0)
            return Math.Sign(byName);

        int byStub = ((int)a.Stub).CompareTo((int)b.Stub);
        return byStub != 0 ? byStub : a.NodeId.CompareTo(b.NodeId);
    }
}
