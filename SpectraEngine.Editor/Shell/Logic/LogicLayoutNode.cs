using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Something that takes room in a column while the graph is laid out: a card,
// or the slot a long wire passes through between cards.
internal sealed class LogicLayoutNode
{
    public LogicLayoutNode(LogicCardFace? face, double height)
    {
        Face = face;
        Height = height;
    }

    // Null for a slot.
    public LogicCardFace? Face { get; }

    // The room it takes from its top. A card's includes its wires to itself.
    public double Height { get; }

    // Its place among its group's nodes: cards by name, then slots. Breaks every tie.
    public int Rank { get; set; }

    public int Column { get; set; }

    // Its place in its column, from the top.
    public int Order { get; set; }

    public double Top { get; set; }

    public double SortKey { get; set; }

    // The wires that reach it from the column before, and the ones that leave it for the next.
    public List<LogicLink> Left { get; } = [];

    public List<LogicLink> Right { get; } = [];

    public bool IsCard => Face is not null;
}
