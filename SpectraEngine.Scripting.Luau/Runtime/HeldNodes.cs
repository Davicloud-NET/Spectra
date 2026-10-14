using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The made nodes that were destroyed and that a script may still hold. Each
// counts against the bound on made nodes until Luau has let go of it. Luau
// counts the handle and not the node, so its collector is told what each
// weighs and is stepped for what they weigh past a goal.
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
    private long _owed;
    private int _idleTicks;

    // A cycle runs that the goal asked for. Until it ends, every byte added
    // steps it. The step every so many ticks does not set this.
    private bool _isCollecting;

    public int Count => _held.Count;

    // What the held nodes weigh, as far as Luau has not collected them.
    public long Bytes => host.Objects.WeightBytes;

    public int PeakCount { get; private set; }

    public long PeakBytes { get; private set; }

    // Twice what was held when the last cycle ended.
    public long Goal { get; private set; } = FirstGoal;

    // How many cycles of the collector ended that this stepped.
    public int Cycles { get; private set; }

    // How many times the collector was stepped for bytes a node added.
    public int Steps { get; private set; }

    // A destroyed node a script holds, or has come to hold again. One that
    // is here already has a new handle, which weighs nothing yet.
    public void Add(SceneNode destroyed)
    {
        if (!_held.Contains(destroyed))
        {
            if (!tree.CountAgain(destroyed))
                return;

            _held.Add(destroyed);
            PeakCount = Math.Max(PeakCount, _held.Count);
        }

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

    // Once a tick. Loose trees that keep `loose` bytes are looked for the
    // same way. True when a step for them ended a cycle, after which Luau
    // has said which of their nodes no script holds.
    public bool EndTick(long loose)
    {
        if ((_held.Count == 0 && loose == 0) || ++_idleTicks < IdleStepTicks)
            return false;

        _idleTicks = 0;
        if (host.IsClosed || Bytes + loose < FirstGoal)
            return false;

        if (!host.StepCollector(Math.Max(1024, host.MemoryUsed / IdleCycleSteps)))
            return false;

        EndCycle();
        return loose > 0;
    }

    private void Weigh(SceneNode node)
    {
        long added = host.Objects.SetWeight(node, NodeWeight.Of(node, tags, attributes));
        PeakBytes = Math.Max(PeakBytes, Bytes);
        if (added <= 0)
            return;

        // Luau may have ended the cycle by itself since the last step.
        if (_isCollecting && host.StepCollector(0))
            EndCycle();

        if (!_isCollecting && Bytes < Goal)
            return;

        // The collector steps by whole kilobytes. Less than one is owed on.
        _owed += added;
        long stepped = Math.Min(_owed, LuauHost.MaxStepBytes) & ~1023L;
        if (stepped == 0)
            return;

        _owed -= stepped;
        _isCollecting = true;
        Steps++;
        if (host.StepCollector(stepped))
            EndCycle();
    }

    private void EndCycle()
    {
        host.Objects.ReleaseCollected();
        _isCollecting = false;
        _owed = 0;
        Goal = Math.Max(FirstGoal, 2 * Bytes);
        Cycles++;
    }
}
