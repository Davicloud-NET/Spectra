using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// The selected nodes as the last snapshot listed them, kept as a set.
internal sealed class LogicSelection
{
    private readonly List<Guid> _listed = [];
    private readonly HashSet<Guid> _ids = [];

    public IReadOnlySet<Guid> Ids => _ids;

    public bool Contains(LogicCard card) => !card.IsStub && _ids.Contains(card.NodeId);

    // Returns whether the selection differs from the one before. Runs on
    // every snapshot, so it allocates nothing when it does not.
    public bool Take(IReadOnlyList<Guid> ids)
    {
        bool same = ids.Count == _listed.Count;
        for (int i = 0; same && i < ids.Count; i++)
            same = ids[i] == _listed[i];

        if (same)
            return false;

        Clear();
        for (int i = 0; i < ids.Count; i++)
        {
            _listed.Add(ids[i]);
            _ids.Add(ids[i]);
        }

        return true;
    }

    public void Clear()
    {
        _listed.Clear();
        _ids.Clear();
    }
}
