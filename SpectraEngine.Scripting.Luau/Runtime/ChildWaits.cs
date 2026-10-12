using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The threads that wait in WaitForChild, each for a child of one name under
// one node. A wait is kept with every other wait, and its wakes come through
// the level's queue: when a child gets the name, when the time is over, and
// once to warn. A wake looks at the node again, so one too many does nothing.
internal sealed class ChildWaits
{
    // What Roblox waits before it warns of a wait with no timeout.
    private const double WarnAfterSeconds = 5;

    private const int FirstTidy = 32;

    private readonly LuauHost _host;
    private readonly EntityWorld _world;
    private readonly WaitingThreads _waiting;
    private readonly List<ChildWait> _waits = [];
    private int _tidyAt = FirstTidy;

    // Set when the queue had no room for a wake. A wait with no timeout has
    // no other wake to look again on.
    private bool _owesWake;

    public ChildWaits(LuauHost host, EntityWorld world, WaitingThreads waiting)
    {
        _host = host;
        _world = world;
        _waiting = waiting;
    }

    // A child gets a name by entering the level, by moving under another
    // node and by being renamed.
    public void Listen()
    {
        Scene scene = _world.Scene;
        scene.NodeAdded += OnNamed;
        scene.NodeReparented += OnNamed;
        scene.NodeRenamed += OnNamed;
    }

    // Any thread may call this, as any may close the runtime.
    public void StopListening()
    {
        Scene scene = _world.Scene;
        scene.NodeAdded -= OnNamed;
        scene.NodeReparented -= OnNamed;
        scene.NodeRenamed -= OnNamed;
    }

    // WaitForChild. The name is the second value and the timeout the third.
    public int Wait(ScriptContext context, LuauStack stack, SceneNode parent)
    {
        if (!stack.TryGetUtf8(2, out ReadOnlySpan<byte> name))
            return ScriptArguments.Wrong(stack, 2, "WaitForChild", "string", isMethod: true);

        double seconds = 0;
        bool hasTimeout = !stack.IsNil(3);
        if (hasTimeout && !stack.TryGetNumber(3, out seconds))
            return ScriptArguments.Wrong(stack, 3, "WaitForChild", "number", isMethod: true);

        Span<char> room = stackalloc char[NameChars.Room];
        if (!NameChars.TryRead(name, room, out ReadOnlySpan<char> wanted))
            return stack.Fail("WaitForChild was handed a name that is not valid UTF-8, which no node has");

        return NodeTree.Child(parent, NodeMatch.Name, wanted) is { } child
            ? ScriptResult.Of(context.PushObject(stack, child))
            : Begin(stack, parent, wanted, hasTimeout, seconds);
    }

    // A wake the queue hands back. False when it is no child wait's.
    public bool TryWake(long token)
    {
        int index = IndexOf(token);
        if (index < 0)
            return false;

        ChildWait wait = _waits[index];
        if (!_waiting.IsWaiting(token))
        {
            Forget(index);
            return true;
        }

        long tick = _world.TickNumber;
        SceneNode? child = NodeTree.Child(wait.Parent, NodeMatch.Name, wait.Name);
        if (child is null && tick < wait.Deadline)
        {
            if (tick >= wait.WarnAt)
            {
                Warn(in wait);
                _waits[index] = wait with { WarnAt = WholeTicks.Never };
            }

            return true;
        }

        // Out of the list first: the thread may wait for the next child at once.
        _waits.RemoveAt(index);
        if (_waiting.TryTake(token, out WaitingThread waiting))
            waiting.Thread.Resume([child is null ? LuauValue.Nil : LuauValue.Object(ScriptTags.Object, child)]);

        return true;
    }

    // A wake the queue dropped, because one tick made more than it sends.
    // Only the wake of an arrival is made in a tick, and the thread must not
    // be lost with it: the next tick looks again. False when it is no child
    // wait's.
    public bool TryDrop(long token)
    {
        if (IndexOf(token) < 0)
            return false;

        if (_world.IsActive)
            QueueWake(token, 1);

        return true;
    }

    // The end of a tick. Asks again for every wake the queue had no room
    // for, by looking at each wait whose child is there.
    public void EndTick()
    {
        if (!_owesWake)
            return;

        _owesWake = false;
        for (int i = 0; i < _waits.Count; i++)
        {
            ChildWait wait = _waits[i];
            if (NodeTree.Child(wait.Parent, NodeMatch.Name, wait.Name) is not null)
                QueueWake(wait.Token, 1);
        }
    }

    private int Begin(LuauStack stack, SceneNode parent, ReadOnlySpan<char> name, bool hasTimeout, double seconds)
    {
        LuauThread thread = _host.ThreadOf(stack);
        if (!thread.IsAlive)
            return stack.Fail("this thread cannot wait for a child");

        // Before the wait is kept: a wake would resume a thread that never
        // yielded wherever it waits by then.
        if (!stack.CanYield)
            return stack.Fail(WaitingThreads.CannotYield);
        if (!_world.IsActive)
            return stack.Fail("the wait was not begun: the level has stopped");

        // With a timeout the one wake is its end. With none it is the warning.
        long now = _world.TickNumber;
        long ticks = hasTimeout ? Math.Max(1, _world.TicksIn(seconds)) : _world.TicksIn(WarnAfterSeconds);
        long due = ticks == WholeTicks.Never ? WholeTicks.Never : now + ticks;

        long token = _waiting.Add(new WaitingThread(thread, now, WakeReply.Nothing, null, 0));
        if (due != WholeTicks.Never && !_world.QueueScriptWake(token, ticks))
        {
            _waiting.TryTake(token, out _);
            return stack.Fail("the wait was not begun: too many threads are waiting in the level");
        }

        Tidy();
        _waits.Add(new ChildWait(
            token, parent, name.ToString(),
            Deadline: hasTimeout ? due : WholeTicks.Never,
            WarnAt: hasTimeout ? WholeTicks.Never : due,
            ScriptReport.Line(stack.State, _host.ScriptOf(stack), ScriptSeverity.Warning, string.Empty)));

        return LuauStack.Yield();
    }

    // A scene event, raised in the middle of whatever changed the level. So
    // it changes nothing and runs no script: it queues the wake.
    private void OnNamed(SceneNode node)
    {
        if (_waits.Count == 0 || node.Parent is not { } parent || !_world.IsActive)
            return;

        try
        {
            for (int i = 0; i < _waits.Count; i++)
            {
                ChildWait wait = _waits[i];
                if (ReferenceEquals(wait.Parent, parent) && string.Equals(wait.Name, node.Name, StringComparison.Ordinal))
                    QueueWake(wait.Token, 0);
            }
        }
        catch (Exception e) // boundary: whoever changed the level must not get a script's failure
        {
            _host.Report(new ScriptMessage(
                ScriptSeverity.Error, $"The script runtime failed: {e.Message}", string.Empty, 0));
        }
    }

    // A wake too many does nothing, so one the queue refused is only owed.
    private void QueueWake(long token, long ticks)
    {
        if (!_world.QueueScriptWake(token, ticks))
            _owesWake = true;
    }

    private void Warn(in ChildWait wait) =>
        _host.Report(wait.Warning with
        {
            Text = $"Infinite yield possible on '{NodeTree.FullName(wait.Parent)}:WaitForChild(\"{wait.Name}\")'",
        });

    // A thread that ended by hand still has its wait among the others.
    private void Forget(int index)
    {
        _waiting.TryTake(_waits[index].Token, out _);
        _waits.RemoveAt(index);
    }

    // No wake may ever come for a wait its thread has left. A thread has
    // left when it was cancelled, has ended, or waits for something else by
    // now. One that coroutine.resume took out of the wait and coroutine.yield
    // parked has not, as far as anything here can tell.
    private void Tidy()
    {
        if (_waits.Count < _tidyAt)
            return;

        for (int i = _waits.Count - 1; i >= 0; i--)
        {
            if (!_waiting.IsWaiting(_waits[i].Token))
                Forget(i);
        }

        _tidyAt = Math.Max(FirstTidy, _waits.Count * 2);
    }

    private int IndexOf(long token)
    {
        for (int i = 0; i < _waits.Count; i++)
        {
            if (_waits[i].Token == token)
                return i;
        }

        return -1;
    }
}
