using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The made nodes that were destroyed and that a script may still hold. What
// a script holds of one keeps the whole node, so each counts against the
// bound on made nodes until Luau has let go of it.
//
// Luau's collector counts the handle and not the node, so it is told what
// each node weighs. Under the goal nothing is asked of it. Past the goal it
// works for every byte a destroyed node adds, as it does for a byte a script
// allocates, until its cycle ends. The goal is then twice what was still
// held, so a script that holds much is not collected over and over.
internal sealed class HeldNodes(LuauHost host, TreeWrites tree, TagTable tags, AttributeTable attributes)
{
    // Held bytes under this ask for no collection.
    public const long FirstGoal = 256 * 1024;

    // While scripts hold more than the first goal, the collector takes a
    // step every so many ticks, whatever the scripts do, and a cycle ends in
    // about half so many steps. What a script let go of without another
    // Destroy is found by this.
    private const int IdleStepTicks = 64;
    private const int IdleCycleSteps = 64;

    // Each is in the host's objects too, and leaves when it leaves those.
    // So this keeps no node that Luau has collected.
    private readonly HashSet<SceneNode> _held = new(ReferenceEqualityComparer.Instance);
    private long _goal = FirstGoal;
    private long _owed;
    private int _idleTicks;
    private bool _isCollecting;

    public int Count => _held.Count;

    // What the held nodes weigh, as far as Luau has not collected them.
    public long Bytes => host.Objects.WeightBytes;

    public int PeakCount { get; private set; }

    public long PeakBytes { get; private set; }

    // How many cycles of the collector ended that this asked for.
    public int Cycles { get; private set; }

    // A destroyed node a script holds, or has come to hold again.
    public void Add(SceneNode destroyed)
    {
        if (!tree.CountAgain(destroyed))
            return;

        _held.Add(destroyed);
        PeakCount = Math.Max(PeakCount, _held.Count);
        Weigh(destroyed);
    }

    // A script gave a destroyed node a tag or an attribute, or took one.
    public void Changed(SceneNode destroyed)
    {
        if (_held.Contains(destroyed))
            Weigh(destroyed);
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

    // Once a tick.
    public void EndTick()
    {
        if (_held.Count == 0 || ++_idleTicks < IdleStepTicks)
            return;

        _idleTicks = 0;
        if (host.IsClosed || Bytes < FirstGoal)
            return;

        _isCollecting = true;
        Step(Math.Max(1024, host.MemoryUsed / IdleCycleSteps));
    }

    private void Weigh(SceneNode node)
    {
        long added = host.Objects.SetWeight(node, NodeWeight.Of(node, tags, attributes));
        long bytes = Bytes;
        PeakBytes = Math.Max(PeakBytes, bytes);
        if (added <= 0)
            return;

        // Luau may have ended the cycle by itself since the last step.
        if (_isCollecting && host.StepCollector(0))
            EndCycle();

        if (!_isCollecting && Bytes < _goal)
            return;

        _isCollecting = true;
        _owed += added;
        Step(_owed);
    }

    private void Step(long bytes)
    {
        long stepped = Math.Min(bytes, LuauHost.MaxStepBytes) & ~1023L;
        if (stepped == 0)
            return;

        _owed = Math.Max(0, _owed - stepped);
        if (host.StepCollector(stepped))
            EndCycle();
    }

    private void EndCycle()
    {
        host.Objects.ReleaseCollected();
        _isCollecting = false;
        _owed = 0;
        _goal = Math.Max(FirstGoal, 2 * Bytes);
        Cycles++;
    }
}
