using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The trees scripts made that are in no level: each by its top node. A
// script that makes a node and lets go of it destroys nothing, so the node
// would count for good. Luau says which nodes scripts still hold, and a
// tree none of whose nodes is held is forgotten.
internal sealed class LooseNodes(LuauHost host, TreeWrites tree, Action<SceneNode> forget)
{
    // With fewer trees than this none is looked over.
    private const int FirstLookOver = 64;

    private readonly HashSet<SceneNode> _tops = new(ReferenceEqualityComparer.Instance);
    private readonly List<SceneNode> _unreached = [];
    private readonly List<SceneNode> _walk = [];
    private int _lookOverAt = FirstLookOver;
    private bool _isSweeping;

    public int Count => _tops.Count;

    public void Add(SceneNode top) => _tops.Add(top);

    public void Remove(SceneNode node) => _tops.Remove(node);

    // A made node got a parent or lost it.
    public void Note(SceneNode made)
    {
        if (made.Parent is null && !TreeWrites.IsDestroyed(made))
            _tops.Add(made);
        else
            _tops.Remove(made);
    }

    // Each time there are twice as many trees as were left the last time,
    // those no script can reach are let go.
    public void LookOver()
    {
        if (_tops.Count < _lookOverAt)
            return;

        Sweep();
        _lookOverAt = Math.Max(FirstLookOver, _tops.Count * 2);
    }

    // Forgets every tree no script holds a node of. Which those are is for
    // Luau to say, and a full collection makes it say so now. True when
    // one was forgotten.
    public bool Sweep()
    {
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

            for (int i = 0; i < _unreached.Count; i++)
                Drop(_unreached[i]);

            return _unreached.Count > 0;
        }
        finally
        {
            _unreached.Clear();
            _isSweeping = false;
        }
    }

    private void Drop(SceneNode top)
    {
        _tops.Remove(top);
        tree.Forget(top);
        Fill(top);
        for (int i = 0; i < _walk.Count; i++)
            forget(_walk[i]);

        _walk.Clear();
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
