using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// The entities that have a card on show, in the graph's order. A window asks
// the engine for a line of state for each of them.
internal sealed class LogicShownEntities
{
    public Guid[] Ids { get; private set; } = [];

    // Returns whether the entities differ from the ones before. Compared in
    // place: most rescopes show the same cards.
    public bool Take(IReadOnlyList<LogicCard> cards)
    {
        int count = 0;
        bool same = true;

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].IsStub)
                continue;

            same = same && count < Ids.Length && Ids[count] == cards[i].NodeId;
            count++;
        }

        if (same && count == Ids.Length)
            return false;

        var ids = new Guid[count];
        for (int i = 0, at = 0; i < cards.Count; i++)
        {
            if (!cards[i].IsStub)
                ids[at++] = cards[i].NodeId;
        }

        Ids = ids;
        return true;
    }
}
