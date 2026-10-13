using System.Runtime.InteropServices;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Puts nodes into the order of the tree they are in: a node before what is
// below it, and children in their parent's order. It walks up from each
// node and then down again along those paths only, so it is paid by the
// nodes handed in and by the children of the nodes where their paths part,
// never by the rest of the level.
internal sealed class TreeOrder
{
    private readonly Dictionary<SceneNode, Mark> _marks = [];
    private readonly List<SceneNode> _tops = [];

    // How many nodes the last sort looked at, on the way up and on the way down.
    public int LookedAt { get; private set; }

    public void Sort(List<SceneNode> nodes)
    {
        LookedAt = 0;
        if (nodes.Count < 2)
            return;

        for (int i = 0; i < nodes.Count; i++)
            MarkWithWhatIsAbove(nodes[i]);

        // A node with no parent is the root, or the top of what left the level.
        nodes.Clear();
        for (int i = 0; i < _tops.Count; i++)
            AddFrom(_tops[i], nodes);

        _marks.Clear();
        _tops.Clear();
    }

    // Up to the first node an earlier walk has passed. Each node on the way
    // counts the children the paths go through, and keeps the first.
    private void MarkWithWhatIsAbove(SceneNode wanted)
    {
        ref Mark own = ref CollectionsMarshal.GetValueRefOrAddDefault(_marks, wanted, out bool wasPassed);
        own.IsWanted = true;
        if (wasPassed)
            return;

        SceneNode child = wanted;
        while (child.Parent is { } parent)
        {
            LookedAt++;
            ref Mark above = ref CollectionsMarshal.GetValueRefOrAddDefault(_marks, parent, out wasPassed);
            if (above.ChildrenOnPaths++ == 0)
                above.OnlyChild = child;
            if (wasPassed)
                return;

            child = parent;
        }

        _tops.Add(child);
    }

    private void AddFrom(SceneNode node, List<SceneNode> ordered)
    {
        while (true)
        {
            Mark mark = _marks[node];
            if (mark.IsWanted)
                ordered.Add(node);

            if (mark.ChildrenOnPaths != 1 || mark.OnlyChild is null)
            {
                AddFromChildren(node, mark.ChildrenOnPaths, ordered);
                return;
            }

            // One path down: no need to look the children over.
            node = mark.OnlyChild;
        }
    }

    // Where paths part, the children say which comes first.
    private void AddFromChildren(SceneNode node, int onPaths, List<SceneNode> ordered)
    {
        IReadOnlyList<SceneNode> children = node.Children;
        for (int i = 0; i < children.Count && onPaths > 0; i++)
        {
            LookedAt++;
            if (!_marks.ContainsKey(children[i]))
                continue;

            onPaths--;
            AddFrom(children[i], ordered);
        }
    }

    private struct Mark
    {
        public bool IsWanted;
        public int ChildrenOnPaths;
        public SceneNode? OnlyChild;
    }
}
