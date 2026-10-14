using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The trees scripts made that are in no level: each by its top node. A
// script that makes a node and lets go of it destroys nothing, so the node
// would count for good. Luau says which nodes scripts still hold, and a
// tree none of whose nodes is held is forgotten.
internal sealed class LooseNodes(
    LuauHost host, TreeWrites tree, Action<SceneNode> forget, Func<SceneNode, long> bytesOf)
{
    // With fewer trees than this none is looked over.
    private const int FirstLookOver = 64;

    // A look-over also comes once scripts have added this share of what Luau
    // holds to loose trees. A full collection takes time for each byte Luau
    // holds, so the time this costs for a byte added is the same at any size
    // of heap, and what waits is at most this share of it.
    private const int HeapShare = 8;

    private readonly HashSet<SceneNode> _tops = new(ReferenceEqualityComparer.Instance);

    // Tops of trees one of whose nodes Luau let go of since the last look.
    // Each is looked at alone, and needs no collection.
    private readonly HashSet<SceneNode> _released = new(ReferenceEqualityComparer.Instance);
    private readonly List<SceneNode> _unreached = [];
    private readonly List<SceneNode> _walk = [];
    private int _lookOverAt = FirstLookOver;
    private bool _isSweeping;

    // What scripts added to loose trees since the last look-over, and what
    // the trees left by it kept, as far as none has been let go of since.
    private long _grown;
    private long _kept;

    public int Count => _tops.Count;

    // About what the loose trees keep that scripts set on them, at most.
    public long Bytes => _tops.Count == 0 ? 0 : _kept + _grown;

    // The most trees there were at one time since the level started.
    public int PeakCount { get; private set; }

    public void Add(SceneNode top)
    {
        _tops.Add(top);
        PeakCount = Math.Max(PeakCount, _tops.Count);
    }

    public void Remove(SceneNode node) => _tops.Remove(node);

    // A made node got a parent or lost it.
    public void Note(SceneNode made)
    {
        if (made.Parent is null && !TreeWrites.IsDestroyed(made))
            Add(made);
        else
            _tops.Remove(made);
    }

    // A script set more on a node of a loose tree, or a tree with what
    // scripts set on it came to be loose.
    public void Grew(long bytes) => _grown += bytes;

    // Luau let go of a made node in no level. Its tree is looked at when
    // nothing runs inside the collector: at the end of the tick, or before a
    // node is made. Pushes nothing and runs no script.
    public void Released(SceneNode node)
    {
        SceneNode top = node;
        while (top.Parent is { } above)
            top = above;

        if (_tops.Contains(top))
            _released.Add(top);
    }

    // Forgets the trees Luau let go of a node of, when no script holds
    // another node of them.
    public void DropReleased()
    {
        if (_released.Count == 0 || _isSweeping)
            return;

        _isSweeping = true;
        try
        {
            foreach (SceneNode top in _released)
            {
                if (_tops.Contains(top) && !IsHeld(top))
                    _unreached.Add(top);
            }

            _released.Clear();
            for (int i = 0; i < _unreached.Count; i++)
                Drop(_unreached[i]);
        }
        finally
        {
            _unreached.Clear();
            _isSweeping = false;
        }
    }

    // Each time there are twice as many trees as were left the last time,
    // those no script can reach are let go.
    public void LookOver()
    {
        DropReleased();
        if (_tops.Count < _lookOverAt && !IsDueByBytes())
            return;

        Sweep();
        _lookOverAt = Math.Max(FirstLookOver, _tops.Count * 2);
    }

    // Forgets every tree no script holds a node of. Which those are is for
    // Luau to say, and a full collection makes it say so now. True when
    // one was forgotten.
    public bool Sweep()
    {
        _grown = 0;
        if (_tops.Count == 0 || _isSweeping)
            return false;

        host.Collect();
        return DropUnheld();
    }

    // Forgets every tree no script holds a node of, as far as Luau has said.
    public bool DropUnheld()
    {
        // Forgetting a node can end a thread, and ending one can run a
        // script that makes a node and so comes back here.
        if (_tops.Count == 0 || _isSweeping)
            return false;

        _isSweeping = true;
        try
        {
            foreach (SceneNode top in _tops)
            {
                if (!IsHeld(top))
                    _unreached.Add(top);
            }

            _released.Clear();
            for (int i = 0; i < _unreached.Count; i++)
                Drop(_unreached[i]);

            _kept = 0;
            foreach (SceneNode top in _tops)
                _kept += BytesOf(top);

            return _unreached.Count > 0;
        }
        finally
        {
            _unreached.Clear();
            _isSweeping = false;
        }
    }

    // Once a tick, after Luau's report of what it let go of was read.
    public void EndTick()
    {
        DropReleased();
        if (IsDueByBytes())
            Sweep();
    }

    // What waits counts against the memory budget, so near it a look-over
    // comes sooner.
    private bool IsDueByBytes()
    {
        if (_grown < HeldNodes.FirstGoal)
            return false;

        long goal = Math.Max(HeldNodes.FirstGoal, host.MemoryUsed / HeapShare);
        if (host.MemoryBudget > 0)
            goal = Math.Min(goal, Math.Max(HeldNodes.FirstGoal, (host.MemoryBudget - host.HeldBytes) / 2));

        return _grown >= goal;
    }

    private void Drop(SceneNode top)
    {
        _tops.Remove(top);
        tree.Forget(top);
        Fill(top);
        long bytes = 0;
        for (int i = 0; i < _walk.Count; i++)
        {
            bytes += bytesOf(_walk[i]);
            forget(_walk[i]);
        }

        _walk.Clear();
        _kept = Math.Max(0, _kept - bytes);
    }

    private long BytesOf(SceneNode top)
    {
        Fill(top);
        long bytes = 0;
        for (int i = 0; i < _walk.Count; i++)
            bytes += bytesOf(_walk[i]);

        _walk.Clear();
        return bytes;
    }

    private bool IsHeld(SceneNode top)
    {
        Fill(top);
        bool isHeld = false;
        for (int i = 0; i < _walk.Count && !isHeld; i++)
            isHeld = host.Objects.Holds(_walk[i]);

        _walk.Clear();
        return isHeld;
    }

    // The node and all below it. The list is its own queue.
    private void Fill(SceneNode top)
    {
        _walk.Clear();
        _walk.Add(top);
        for (int i = 0; i < _walk.Count; i++)
        {
            IReadOnlyList<SceneNode> children = _walk[i].Children;
            for (int c = 0; c < children.Count; c++)
                _walk.Add(children[c]);
        }
    }
}
