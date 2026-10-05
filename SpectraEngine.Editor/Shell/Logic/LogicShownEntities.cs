using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// The entities that have a card on show, in the graph's order. A window asks
// the engine for a line of state for each of them.
internal sealed class LogicShownEntities
{
    private Guid? _arrived;

    public Guid[] Ids { get; private set; } = [];

    // The first entity with no wires whose card came on show with the last
    // change, or null. Read once: the next read has nothing until another
    // such card comes.
    public Guid? TakeArrived()
    {
        Guid? arrived = _arrived;
        _arrived = null;
        return arrived;
    }

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

        _arrived = FirstNewUnwired(cards);

        var ids = new Guid[count];
        for (int i = 0, at = 0; i < cards.Count; i++)
        {
            if (!cards[i].IsStub)
                ids[at++] = cards[i].NodeId;
        }

        Ids = ids;
        return true;
    }

    // Looked for before Ids is replaced. The set is only built when a card
    // with no wires is on show, which is when one is selected.
    private Guid? FirstNewUnwired(IReadOnlyList<LogicCard> cards)
    {
        HashSet<Guid>? before = null;

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].IsStub || cards[i].IsWired)
                continue;

            before ??= [.. Ids];
            if (!before.Contains(cards[i].NodeId))
                return cards[i].NodeId;
        }

        return null;
    }
}
