using SpectraEngine.Core.Entities;

namespace SpectraEngine.Scripting.Luau.Runtime;

// The task library, and the old wait, spawn and delay. A thread that waits
// is an event in the entity world's queue, so it goes on in one order with
// the wires and the thinks, on a tick and never between two.
internal sealed class TaskLibrary
{
    private const string NoRoom =
        "the wait was not queued: the level has stopped, or too many events are waiting in it";

    private readonly LuauHost _host;
    private readonly EntityWorld _world;
    private readonly WaitingThreads _waiting = new();

    public TaskLibrary(LuauHost host, EntityWorld world)
    {
        _host = host;
        _world = world;

        new ApiTable(host)
            .Function("wait"u8, stack => Wait(stack, WakeReply.Elapsed))
            .Function("spawn"u8, Spawn)
            .Function("defer"u8, stack => Later(stack, "defer", target: 1, ticks: 0, WakeReply.Nothing))
            .Function("delay"u8, stack => Delay(stack, WakeReply.Nothing))
            .Function("cancel"u8, Cancel)
            .SetAsGlobal("task"u8);

        SetGlobal("wait"u8, stack => Wait(stack, WakeReply.ElapsedAndTime));
        SetGlobal("spawn"u8, stack => Later(stack, "spawn", target: 1, ticks: 1, WakeReply.ElapsedAndTime));
        SetGlobal("delay"u8, stack => Delay(stack, WakeReply.ElapsedAndTime));
    }

    // How many threads wait for a tick.
    public int WaitingCount => _waiting.Count;

    // A wait is over: the thread goes on, unless a script cancelled it or
    // gave it another wait since.
    public void Wake(long token)
    {
        if (!_waiting.TryTake(token, out WaitingThread waiting))
            return;

        double elapsed = (_world.TickNumber - waiting.SinceTick) * (double)_world.FixedDeltaTime;
        switch (waiting.Reply)
        {
            case WakeReply.Elapsed:
                waiting.Thread.Resume([elapsed]);
                break;
            case WakeReply.ElapsedAndTime:
                waiting.Thread.Resume([elapsed, (double)_world.Time]);
                break;
            default:
                ResumeWithKept(in waiting);
                break;
        }
    }

    // The queue dropped the wake, so the thread would wait for good.
    public void Drop(long token)
    {
        if (!_waiting.TryTake(token, out WaitingThread waiting))
            return;

        waiting.Arguments?.Dispose();
        waiting.Thread.Cancel();
    }

    // Never the tick it was called on: no time at all still waits for the next.
    private int Wait(LuauStack stack, WakeReply reply)
    {
        if (!ScriptArguments.TryOptionalNumber(stack, 1, 0, out double seconds))
            return ScriptArguments.Wrong(stack, 1, "wait", "number");

        LuauThread thread = _host.ThreadOf(stack);
        if (!thread.IsAlive)
            return stack.Fail("this thread cannot wait");

        return Schedule(new WaitingThread(thread, _world.TickNumber, reply, null, 0), TicksFor(seconds))
            ? LuauStack.Yield()
            : stack.Fail(NoRoom);
    }

    // Runs at once, up to its end or its first wait, and hands back the thread.
    private int Spawn(LuauStack stack)
    {
        int count = stack.Count;
        if (stack.TypeOf(1) == LuauType.Thread)
            return ResumeNow(stack, count);
        if (stack.TypeOf(1) != LuauType.Function)
            return ScriptArguments.Wrong(stack, 1, "spawn", "function or thread");
        if (!_host.Prepare(stack, count, out LuauThread thread))
            return stack.Fail("task.spawn could not make a thread");
        if (!thread.Push(stack))
            return LuauStack.Raise;

        thread.Resume();
        return 1;
    }

    private int ResumeNow(LuauStack stack, int count)
    {
        LuauThread thread = _host.ThreadAt(stack, 1);
        _waiting.Remove(thread);

        return thread.Resume(stack, count - 1) == LuauResume.Refused
            ? stack.Fail("task.spawn cannot resume a thread that is not waiting")
            : 1;
    }

    private int Delay(LuauStack stack, WakeReply reply) =>
        stack.TryGetNumber(1, out double seconds)
            ? Later(stack, "delay", target: 2, TicksFor(seconds), reply)
            : ScriptArguments.Wrong(stack, 1, "delay", "number");

    // Queues a function or a thread to run on a later drain. What follows
    // the target on the stack is what it is handed then.
    private int Later(LuauStack stack, string function, int target, long ticks, WakeReply reply)
    {
        int count = stack.Count;
        LuauType type = stack.TypeOf(target);
        if (type is not (LuauType.Function or LuauType.Thread))
            return ScriptArguments.Wrong(stack, target, function, "function or thread");

        if (type == LuauType.Thread)
            return LaterWith(stack, function, target, ticks, reply);

        if (!_host.Prepare(stack, count - target + 1, out LuauThread thread))
            return stack.Fail($"{function} could not make a thread");
        if (!thread.Push(stack))
            return LuauStack.Raise;
        if (Schedule(new WaitingThread(thread, _world.TickNumber, reply, null, 0), ticks))
            return 1;

        thread.Cancel();
        return stack.Fail(NoRoom);
    }

    // The same for a thread a script already has. It may be in the middle of
    // a call, so what it is handed waits in a table of the host's.
    private int LaterWith(LuauStack stack, string function, int target, long ticks, WakeReply reply)
    {
        int count = stack.Count;
        if (!TryKeepArguments(stack, target + 1, count, out LuauReference? kept))
            return stack.Fail($"{function} could not keep what the thread is to be handed");

        LuauThread thread = _host.ThreadAt(stack, target);
        if (!Schedule(new WaitingThread(thread, _world.TickNumber, reply, kept, count - target), ticks))
        {
            kept?.Dispose();
            return stack.Fail(NoRoom);
        }

        return ScriptResult.Of(stack.PushCopy(target));
    }

    private int Cancel(LuauStack stack)
    {
        if (stack.TypeOf(1) != LuauType.Thread)
            return ScriptArguments.Wrong(stack, 1, "cancel", "thread");

        LuauThread thread = _host.ThreadAt(stack, 1);
        _waiting.Remove(thread);

        // False for a thread that has ended too, which there is nothing to say about.
        return thread.Cancel() || !thread.IsAlive
            ? 0
            : stack.Fail("task.cancel cannot end a thread that is running");
    }

    private void SetGlobal(ReadOnlySpan<byte> name, LuauHostFunction function)
    {
        LuauStack stack = _host.Stack;
        if (!_host.PushFunction(stack, name, function) || !stack.SetGlobal(name))
            throw new InvalidOperationException($"The script API could not be set up: {stack.GetText(-1)}");
    }

    private long TicksFor(double seconds) => Math.Max(1, _world.TicksIn(seconds));

    // A wait too long to count is not queued: the thread waits until the
    // level stops or a script cancels it.
    private bool Schedule(in WaitingThread waiting, long ticks)
    {
        if (!_world.IsActive)
            return false;

        long token = _waiting.Add(in waiting);
        if (ticks == WholeTicks.Never || _world.QueueScriptWake(token, ticks))
            return true;

        _waiting.TryTake(token, out _);
        return false;
    }

    // Packs the values from `first` to `last` into a table the host keeps.
    // Null with no values. False: they could not be kept.
    private bool TryKeepArguments(LuauStack stack, int first, int last, out LuauReference? kept)
    {
        kept = null;
        if (last < first)
            return true;

        if (!stack.NewTable(last - first + 1))
            return false;

        int table = stack.Count;
        for (int index = first; index <= last; index++)
        {
            if (!stack.PushCopy(index) || !stack.SetElement(table, index - first + 1))
                return false;
        }

        kept = _host.Keep(stack, table);
        stack.Pop();
        return kept is not null;
    }

    // Goes on with what a script handed over when it queued the thread.
    private void ResumeWithKept(in WaitingThread waiting)
    {
        if (waiting.Arguments is not { } kept)
        {
            waiting.Thread.Resume();
            return;
        }

        using (kept)
        {
            LuauStack stage = _host.Stack;
            int below = stage.Count;
            bool isStaged = kept.Push(stage);
            for (int n = 1; isStaged && n <= waiting.ArgumentCount; n++)
                isStaged = stage.GetElement(below + 1, n);

            // Resume takes the values. The table is left, or whatever a
            // failed push put there.
            if (isStaged)
                waiting.Thread.Resume(stage, waiting.ArgumentCount);

            stage.Pop(stage.Count - below);
        }
    }
}
