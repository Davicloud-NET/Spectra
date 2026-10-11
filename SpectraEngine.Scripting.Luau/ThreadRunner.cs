using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau;

// Starts, resumes and ends the host's threads, and keeps the table their
// handles point into. Script code runs inside sl_resume and sl_cancel and can
// start and end threads itself, so a slot found before such a call is looked
// up again after it.
internal sealed unsafe class ThreadRunner
{
    private readonly LuauHost _host;
    private readonly nint _main;
    private readonly nint _results;

    private ThreadSlot[] _slots = new ThreadSlot[16];
    private int _count;
    private int _firstFree = -1;

    // The thread that ended last. What it returned sits on the results thread
    // until the next one ends.
    private LuauThread _finished;
    private int _finishedCount;

    internal ThreadRunner(LuauHost host, nint main)
    {
        _host = host;
        _main = main;
        _results = Shim.sl_results(main);
    }

    internal LuauThread Add(nint state, int reference, LuauScript? script)
    {
        int index = _firstFree;
        if (index >= 0)
            _firstFree = _slots[index].NextFree;
        else
        {
            if (_count == _slots.Length)
                Array.Resize(ref _slots, _count * 2);
            index = _count++;
        }

        ref ThreadSlot slot = ref _slots[index];
        slot.State = state;
        slot.Reference = reference;
        slot.Script = script;
        slot.ValueCount = 0;
        slot.IsLetGo = false;

        Lua.lua_setthreaddata(state, ThreadData.Pack(index, script));
        return new LuauThread(_host, index, slot.Generation);
    }

    internal bool TryFind(LuauThread thread, out int index)
    {
        index = thread.Slot;
        return ReferenceEquals(thread.Host, _host)
            && (uint)index < (uint)_count
            && _slots[index].State != 0
            && _slots[index].Generation == thread.Generation;
    }

    internal LuauScript? ScriptOf(LuauThread thread) => TryFind(thread, out int index) ? _slots[index].Script : null;

    // The thread a host function runs on. One the host did not start, such as
    // a coroutine a script made, is taken in here.
    internal LuauThread Of(nint state)
    {
        int index = ThreadData.SlotOf(state);
        if ((uint)index < (uint)_count && _slots[index].State == state)
            return new LuauThread(_host, index, _slots[index].Generation);

        int reference;
        if (Shim.sl_pushthread(state) != LuaStatus.Ok)
        {
            Shim.sl_pop(state, 1);
            return default;
        }

        LuaStatus kept = Shim.sl_keep(state, -1, &reference);
        Shim.sl_pop(state, kept == LuaStatus.Ok ? 1 : 2);
        return kept == LuaStatus.Ok ? Add(state, reference, _host.ScriptOf(state)) : default;
    }

    // Runs the function under the top `count - 1` values of `source` on a
    // thread of its own. Takes all `count` values.
    internal LuauResume Start(nint source, int count, LuauScript? script, out LuauThread thread)
    {
        thread = default;
        if (_host.IsClosed)
            return LuauResume.Refused;

        nint state;
        int reference;
        if (script is { IsSwitchedOff: true } || !Spawn(script, &state, &reference))
        {
            Shim.sl_pop(source, count);
            return LuauResume.Refused;
        }

        thread = Add(state, reference, script);
        return Resume(thread, source, count);
    }

    // Takes the top `count` values of `source`, whatever the outcome.
    internal LuauResume Resume(LuauThread thread, nint source, int count)
    {
        if (_host.IsClosed)
            return LuauResume.Refused;

        if (!TryFind(thread, out int index) || _slots[index].Script is { IsSwitchedOff: true })
        {
            Shim.sl_pop(source, count);
            return LuauResume.Refused;
        }

        nint state = _slots[index].State;
        int results;
        LuaStatus status = Shim.sl_resume(state, source, count, &results);

        LuauResume outcome = status switch
        {
            LuaStatus.Yield => Yielded(thread, results),
            LuaStatus.Ok => Finished(thread, results),
            LuaStatus.Refused => Refused(thread, state),
            _ => Failed(thread, state),
        };

        _host.AfterCall();
        return outcome;
    }

    // Ends a thread that waits. False when it is running, or already gone.
    internal bool Cancel(LuauThread thread)
    {
        if (_host.IsClosed || !TryFind(thread, out int index))
            return false;

        bool isEnded = Shim.sl_cancel(_main, _slots[index].State) == LuaStatus.Ok;
        if (!isEnded)
            Shim.sl_pop(_main, 1);
        else if (TryFind(thread, out index))
            Remove(index);

        _host.AfterCall();
        return isEnded;
    }

    // A thread that runs is in no script's hands and on no stack: the slot's
    // reference is all that keeps it from Luau's collector. So one that is
    // let go while it runs keeps its slot until it next waits.
    internal void Release(LuauThread thread)
    {
        if (_host.IsClosed || !TryFind(thread, out int index))
            return;

        if (IsRunning(_slots[index].State))
            _slots[index].IsLetGo = true;
        else
            Remove(index);
    }

    // Gives a script's own reference to its top-level thread to the thread's
    // slot, for a thread that is still there when its script lets go of it.
    internal bool TryHandOver(LuauThread thread, int reference)
    {
        if (!TryFind(thread, out int index))
            return false;

        _slots[index].Reference = reference;
        return true;
    }

    // Ends every thread of a script, so that what they hold can be freed. One
    // that is running refuses and is left: it finds its script switched off
    // when it next waits.
    internal void EndAllOf(LuauScript script)
    {
        for (int index = 0; index < _count; index++)
        {
            if (_slots[index].State == 0 || !ReferenceEquals(_slots[index].Script, script))
                continue;

            LuauThread thread = new(_host, index, _slots[index].Generation);
            if (Shim.sl_cancel(_main, _slots[index].State) != LuaStatus.Ok)
                Shim.sl_pop(_main, 1);
            else if (TryFind(thread, out int found))
                Remove(found);
        }
    }

    // Pushes the thread itself, as a value a script can hold, or nil for one
    // that is gone. A script's top-level thread is kept by its script.
    internal bool Push(LuauThread thread, nint stack)
    {
        if (!TryFind(thread, out int index))
            return Shim.sl_pushnil(stack) == LuaStatus.Ok;

        ref ThreadSlot slot = ref _slots[index];
        int reference = slot.Reference != 0 ? slot.Reference : slot.Script?.ThreadReference ?? Lua.NilReference;
        return Shim.sl_getref(stack, reference) == LuaStatus.Ok;
    }

    internal LuauStack ValuesOf(LuauThread thread, out int count)
    {
        if (!_host.IsClosed && thread == _finished)
        {
            count = _finishedCount;
            return new LuauStack(_results);
        }

        if (!_host.IsClosed && TryFind(thread, out int index))
        {
            count = _slots[index].ValueCount;
            return new LuauStack(_slots[index].State);
        }

        throw new InvalidOperationException(
            "The thread is gone and so are its values. Read what a thread returned before another thread ends.");
    }

    private bool Spawn(LuauScript? script, nint* state, int* reference)
    {
        if (Shim.sl_spawn(_main, script?.ThreadReference ?? 0, 0, state, reference) == LuaStatus.Ok)
            return true;

        Shim.sl_pop(_main, 1);
        return false;
    }

    private LuauResume Yielded(LuauThread thread, int results)
    {
        if (!TryFind(thread, out int index))
            return LuauResume.Yielded;

        // Its script was switched off while it ran, when it could not be ended.
        if (_slots[index].Script is { IsSwitchedOff: true })
        {
            if (Shim.sl_cancel(_main, _slots[index].State) != LuaStatus.Ok)
                Shim.sl_pop(_main, 1);
            if (TryFind(thread, out index))
                Remove(index);
        }
        else if (_slots[index].IsLetGo)
        {
            Remove(index);
        }
        else
        {
            _slots[index].ValueCount = results;
        }

        return LuauResume.Yielded;
    }

    // Luau calls a thread "normal" while it runs and is not the one that asks.
    private bool IsRunning(nint state) => Lua.lua_costatus(_main, state) is LuaCoStatus.Running or LuaCoStatus.Normal;

    private LuauResume Finished(LuauThread thread, int results)
    {
        _finished = thread;
        _finishedCount = results;
        if (TryFind(thread, out int index))
            Remove(index);
        return LuauResume.Finished;
    }

    // The shim would not resume it. A thread that ended some other way, such
    // as through coroutine.resume in a script, has no use for its slot.
    private LuauResume Refused(LuauThread thread, nint state)
    {
        if (TryFind(thread, out int index) && Lua.lua_costatus(_main, state) is LuaCoStatus.Finished or LuaCoStatus.Failed)
            Remove(index);
        return LuauResume.Refused;
    }

    // The error value is on the thread's stack and its frames are still there.
    private LuauResume Failed(LuauThread thread, nint state)
    {
        LuauScript? script = ScriptOf(thread);
        int stop = Shim.sl_takestop(_main);
        ScriptMessage message = ScriptReport.Failure(state, _main, script);

        // A thread that failed keeps its frames and all they hold for as long
        // as anything refers to it, and its script does. This empties it.
        if (Shim.sl_cancel(_main, state) != LuaStatus.Ok)
            Shim.sl_pop(_main, 1);
        if (TryFind(thread, out int index))
            Remove(index);

        if (stop != 0)
        {
            script?.SwitchOff();
            message = message with { Text = _host.Explain(stop) };
        }

        _host.Report(in message);
        return LuauResume.Failed;
    }

    private void Remove(int index)
    {
        ref ThreadSlot slot = ref _slots[index];
        if (slot.Reference != 0)
            Lua.lua_unref(_main, slot.Reference);

        slot.State = 0;
        slot.Reference = 0;
        slot.Script = null;
        slot.Generation++;
        slot.NextFree = _firstFree;
        _firstFree = index;
    }
}
