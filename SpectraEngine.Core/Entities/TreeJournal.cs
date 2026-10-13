using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace SpectraEngine.Core.Entities;

// How a level's tree was before the level changed it: the children of each
// parent, as they were when the level first added to them, took from them
// or moved among them. Stop puts every such list back, so a node the level
// made is gone and one it destroyed or moved is where it was. It also
// counts the nodes the level made that are still there.
internal sealed class TreeJournal
{
    private readonly Dictionary<SceneNode, SceneNode[]> _childrenOf = new(ReferenceEqualityComparer.Instance);

    // In the order they were first touched, so a restore goes the same way
    // on every run.
    private readonly List<SceneNode> _parents = [];
    private readonly List<SceneNode> _destroyed = [];

    // Nodes the level made and has not destroyed, in its tree or out of it.
    public int MadeCount { get; set; }

    public int TouchedParentCount => _parents.Count;

    public int DestroyedCount => _destroyed.Count;

    // Call before a parent's list of children changes. Only the first call
    // for a parent records. A node the level made has no list to put back.
    public void RecordChildren(SceneNode parent)
    {
        if (parent.IsMadeByLevel || _childrenOf.ContainsKey(parent))
            return;

        IReadOnlyList<SceneNode> children = parent.Children;
        var first = new SceneNode[children.Count];
        for (int i = 0; i < first.Length; i++)
            first[i] = children[i];

        _childrenOf.Add(parent, first);
        _parents.Add(parent);
    }

    // A node of the level as it was built was destroyed. Stop lets it take
    // a parent again.
    public void NoteDestroyed(SceneNode node)
    {
        if (!node.IsMadeByLevel)
            _destroyed.Add(node);
    }

    // Every recorded list goes back as it was. A node that never moved is
    // not touched, so a world brush stays in the scene and nothing recompiles.
    public void Restore()
    {
        ExceptionDispatchInfo? first = null;

        for (int i = 0; i < _destroyed.Count; i++)
            _destroyed[i].IsDestroyed = false;

        // All strangers leave before any child goes home. A child put back
        // under a node that still sits below it would be a cycle.
        for (int i = 0; i < _parents.Count; i++)
            Try(RemoveStrangers, _parents[i], ref first);
        for (int i = 0; i < _parents.Count; i++)
            Try(PutInOrder, _parents[i], ref first);

        Clear();
        first?.Throw();
    }

    public void Clear()
    {
        _childrenOf.Clear();
        _parents.Clear();
        _destroyed.Clear();
        MadeCount = 0;
    }

    private void Try(Action<SceneNode, SceneNode[]> step, SceneNode parent, ref ExceptionDispatchInfo? first)
    {
        try
        {
            step(parent, _childrenOf[parent]);
        }
        catch (Exception failure)
        {
            // Thrown again by Restore. The other lists go back first, or
            // they stay as the level left them and the next save writes that.
            first ??= ExceptionDispatchInfo.Capture(failure);
        }
    }

    // Takes away every child the parent did not have at first: a node the
    // level made, or one that came from another parent.
    private static void RemoveStrangers(SceneNode parent, SceneNode[] first)
    {
        if (HasChildren(parent, first))
            return;

        var home = new HashSet<SceneNode>(first, ReferenceEqualityComparer.Instance);
        for (int i = parent.Children.Count - 1; i >= 0; i--)
        {
            SceneNode child = parent.Children[i];
            if (!home.Contains(child))
                parent.RemoveChild(child);
        }
    }

    // Only a child that is out of place is moved. InsertChild takes it from
    // wherever the level left it.
    private static void PutInOrder(SceneNode parent, SceneNode[] first)
    {
        for (int i = 0; i < first.Length; i++)
        {
            if (i >= parent.Children.Count || !ReferenceEquals(parent.Children[i], first[i]))
                parent.InsertChild(i, first[i]);
        }
    }

    private static bool HasChildren(SceneNode parent, SceneNode[] first)
    {
        IReadOnlyList<SceneNode> children = parent.Children;
        if (children.Count != first.Length)
            return false;

        for (int i = 0; i < first.Length; i++)
        {
            if (!ReferenceEquals(children[i], first[i]))
                return false;
        }

        return true;
    }
}
