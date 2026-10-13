using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The made nodes that were destroyed and that a script may still hold. What
// a script holds of one keeps the whole node, so each counts against the
// bound on made nodes until Luau has let go of it.
internal sealed class HeldNodes(LuauHost host, TreeWrites tree)
{
    // Each is in the host's objects too, and leaves when it leaves those.
    // So this keeps no node that Luau has collected.
    private readonly HashSet<SceneNode> _held = new(ReferenceEqualityComparer.Instance);

    public int Count => _held.Count;

    // A destroyed node a script holds, or has come to hold again.
    public void Add(SceneNode destroyed)
    {
        if (tree.CountAgain(destroyed))
            _held.Add(destroyed);
    }

    // No script holds the node any more.
    public void Released(SceneNode node)
    {
        if (_held.Remove(node))
            tree.Forget(node);
    }

    // For when there is no room: a full collection says now which nodes no
    // script holds.
    public void Sweep()
    {
        if (_held.Count > 0)
            host.Collect();
    }
}
