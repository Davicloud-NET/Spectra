using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The made nodes that were destroyed and that a script may still hold. What
// a script holds of one keeps the whole node, so each counts against the
// bound on made nodes until Luau has let go of it.
internal sealed class HeldNodes(LuauHost host, TreeWrites tree)
{
    // With fewer nodes than this none is looked over.
    private const int FirstLookOver = 64;

    private readonly List<SceneNode> _held = [];
    private int _lookOverAt = FirstLookOver;

    public int Count => _held.Count;

    // A destroyed node a script holds, or has come to hold again.
    public void Add(SceneNode destroyed)
    {
        if (tree.CountAgain(destroyed))
            _held.Add(destroyed);
    }

    // Each time there are twice as many as were left the last time, those
    // Luau has collected in its own time stop counting.
    public void LookOver()
    {
        if (_held.Count < _lookOverAt)
            return;

        LetGo();
        _lookOverAt = Math.Max(FirstLookOver, _held.Count * 2);
    }

    // For when there is no room: a full collection says now which nodes no
    // script holds. True when one was let go.
    public bool Sweep()
    {
        if (_held.Count == 0)
            return false;

        host.Collect();
        return LetGo();
    }

    // Stops counting every node whose handle Luau has collected.
    public bool LetGo()
    {
        host.Objects.ReleaseCollected();

        int kept = 0;
        for (int i = 0; i < _held.Count; i++)
        {
            SceneNode node = _held[i];
            if (host.Objects.Holds(node))
                _held[kept++] = node;
            else
                tree.Forget(node);
        }

        bool letGo = kept < _held.Count;
        _held.RemoveRange(kept, _held.Count - kept);
        return letGo;
    }
}
