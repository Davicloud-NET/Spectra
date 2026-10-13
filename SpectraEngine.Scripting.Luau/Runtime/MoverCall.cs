using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau.Runtime;

// A script's mover as the host calls it: its function, and one thread kept
// to run it on every tick. A thread made for each call would leave Luau's
// collector 1.3 KB a tick. Resumed and not called, so an error still has
// its frames and a wait is told from an end.
internal sealed unsafe class MoverCall
{
    public const string Waited = "The mover waited, and a mover has to finish in the tick that calls it";

    public const string Returned = "The mover returned a value. A mover changes the state it is handed and returns nothing";

    private readonly LuauHost _host;
    private readonly LuauFunction _function;
    private nint _thread;
    private int _reference;
    private bool _isRunning;
    private bool _isLetGo;

    private MoverCall(LuauHost host, LuauFunction function, nint thread, int reference)
    {
        _host = host;
        _function = function;
        _thread = thread;
        _reference = reference;
    }

    public LuauScript? Script => _function.Script;

    // Null when Luau made no thread.
    public static MoverCall? Make(LuauHost host, LuauFunction function)
    {
        nint main = host.Stack.State;
        nint thread;
        int reference;
        if (Shim.sl_spawn(main, function.Script?.ThreadReference ?? 0, 0, &thread, &reference) != LuaStatus.Ok)
        {
            Shim.sl_pop(main, 1);
            return null;
        }

        // No slot of the host's: the thread is this call's alone. The
        // script, so what the mover prints names its file.
        Lua.lua_setthreaddata(thread, ThreadData.Pack(-1, function.Script));
        return new MoverCall(host, function, thread, reference);
    }

    // Calls the mover with what it is handed and the step. False when it
    // did not return: `failure` then says why, or is null because the level
    // stopped under the call. After a failure it is not called again.
    public bool TryRun(ReadOnlySpan<LuauReference> handed, double step, out ScriptMessage? failure)
    {
        failure = null;
        if (_host.IsClosed)
            return false;

        if (_thread == 0 || Script is { IsSwitchedOff: true })
        {
            failure = Message("The mover's script was switched off");
            return false;
        }

        LuauStack stage = _host.Stack;
        if (!TryStage(stage, handed, step))
        {
            failure = Message("The mover could not be called");
            return false;
        }

        int results;
        _isRunning = true;
        LuaStatus status = Shim.sl_resume(_thread, stage.State, handed.Length + 2, &results);
        _isRunning = false;
        _host.AfterCall();

        // The state may be gone, and its stacks with it.
        if (_host.IsClosed)
            return false;

        if (status == LuaStatus.Ok && results == 0)
        {
            if (_isLetGo)
                End();
            return true;
        }

        // A state handed back would be dropped, and the player would stand
        // where it stood with nothing said.
        failure = status == LuaStatus.Ok ? Message(Returned) : WhyNot(status, stage.State);
        End();
        return false;
    }

    // For a mover another took the place of. One that is running goes on to
    // its end first: only this keeps its thread from Luau's collector.
    public void LetGo()
    {
        _isLetGo = true;
        if (!_isRunning && !_host.IsClosed)
            End();
    }

    // Every push leaves one value: what was asked for, or why it was not pushed.
    private bool TryStage(LuauStack stage, ReadOnlySpan<LuauReference> handed, double step)
    {
        int staged = 1;
        bool isStaged = _function.Push(stage);
        for (int i = 0; isStaged && i < handed.Length; i++)
        {
            isStaged = handed[i].Push(stage);
            staged++;
        }

        if (isStaged)
        {
            isStaged = stage.Push(step);
            staged++;
        }

        if (!isStaged)
            stage.Pop(staged);

        return isStaged;
    }

    private ScriptMessage WhyNot(LuaStatus status, nint main)
    {
        if (status == LuaStatus.Yield)
            return ScriptReport.Standing(_thread, Script, Waited);
        if (status == LuaStatus.Refused)
            return Message("The mover's thread did not run");

        int stop = Shim.sl_takestop(main);
        ScriptMessage message = ScriptReport.Failure(_thread, main, Script);
        if (stop == 0)
            return message with { Text = $"The mover failed: {message.Text}" };

        // As for any script the host stops: it is switched off for good.
        if (Script is { } script)
        {
            script.StopReason = stop;
            script.SwitchOff();
        }

        return message with { Text = $"The mover was stopped: {_host.Explain(stop)}" };
    }

    private ScriptMessage Message(string text) =>
        new(ScriptSeverity.Error, text, Script?.Name ?? string.Empty, 0);

    // Ends the thread, which empties it, and lets it and the function go.
    private void End()
    {
        nint thread = _thread;
        if (thread == 0)
            return;

        _thread = 0;
        nint main = _host.Stack.State;

        // A wait took the thread into the host's table, which then holds it too.
        if (ThreadData.SlotOf(thread) >= 0)
            _host.Threads.Cancel(_host.Threads.Of(thread));
        else if (Shim.sl_cancel(main, thread) != LuaStatus.Ok)
            Shim.sl_pop(main, 1);

        if (_host.IsClosed)
            return;

        Lua.lua_unref(main, _reference);
        _function.Dispose();
    }
}
