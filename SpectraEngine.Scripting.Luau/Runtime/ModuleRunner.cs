using System.Diagnostics;
using System.Globalization;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Runs a module's file, inside the require that asked for it first, and
// leaves on the module what came of it: the value it returned, or how it
// failed. A failure is reported once, where it happened.
internal sealed class ModuleRunner(LuauHost host, NodeNamingSink sink, WaitingThreads waiting)
{
    // Roblox's words for the two.
    public const string NotOneValue = "Module code did not return exactly one value";
    public const string LoadFailed = "Requested module experienced an error while loading";

    public const string Waited =
        "a module cannot wait before it returns. Start a thread with task.spawn for what has to wait";

    public const string RunsAgain = "The module runs again when it is next asked for";

    // The failed module whose failure an asker was handed last.
    private ScriptModule? _handedOn;

    // How long the modules took that the one running now asked for.
    private TimeSpan _asked;

    // The error for whoever asks for a module that failed.
    public string Refusal(ScriptModule failed)
    {
        _handedOn = failed;
        return Sentence(failed);
    }

    public void Run(ScriptModule module, byte[] text)
    {
        TimeSpan askedBefore = _asked;
        bool heldBefore = sink.HoldsErrors;
        long started = Stopwatch.GetTimestamp();

        _asked = TimeSpan.Zero;
        sink.HoldsErrors = true;
        try
        {
            ScriptMessage? failure = Start(module, text, out LuauScript? script);
            if (failure is { } said)
                Fail(module, script, in said, Stopwatch.GetElapsedTime(started) - _asked);
        }
        finally
        {
            sink.Flush();
            sink.HoldsErrors = heldBefore;
            _asked = askedBefore + Stopwatch.GetElapsedTime(started);
        }
    }

    // Null when the module returned its one value, which it then keeps.
    // Otherwise how it failed, which nobody has been told yet.
    private ScriptMessage? Start(ScriptModule module, byte[] text, out LuauScript? script)
    {
        script = host.Load(module.Path, text);
        if (script is null)
            return Said(module);
        if (module.Node is { } node && !script.SetGlobal("script"u8, LuauValue.Object(ScriptTags.Object, node)))
            return Error(module, "the module could not be given its node");

        return script.Start() switch
        {
            LuauResume.Finished => Keep(module, script.Thread),
            LuauResume.Yielded => EndWait(script),
            LuauResume.Failed => Said(module),
            _ => Error(module, "the module could not be started"),
        };
    }

    // What the host reported when the module did not compile or failed. It
    // reports that last, so it is the error the sink holds.
    private ScriptMessage Said(ScriptModule module) =>
        sink.TakeHeld() ?? Error(module, "the module did not run to its end");

    private ScriptMessage? Keep(ScriptModule module, LuauThread thread)
    {
        if (thread.ValueCount != 1)
            return Error(module, NotOneValue);

        module.Value = host.Keep(thread.Values, 1);
        return module.Value is null ? Error(module, "what the module returned could not be kept") : null;
    }

    // Ends the module's thread where it waits, and its wait with it. The
    // error is at the module's own line, over the calls that led to the wait.
    private ScriptMessage EndWait(LuauScript script)
    {
        LuauThread thread = script.Thread;
        if (!thread.IsAlive)
            return new ScriptMessage(ScriptSeverity.Error, Waited, script.Name, 0);

        ScriptMessage message = ScriptReport.Standing(thread.Values.State, script, Waited);
        waiting.Remove(thread);
        thread.Cancel();
        return message;
    }

    private void Fail(ScriptModule module, LuauScript? script, in ScriptMessage said, TimeSpan own)
    {
        // It failed for the failure of a module it asked for. That one was
        // reported, so this one is not, and its askers get the same failure.
        if (_handedOn is { } cause && said.Text == Sentence(cause))
        {
            module.Failure = cause.Failure;
            module.IsKept = cause.IsKept;
            return;
        }

        ScriptMessage failure = script is { StopReason: not 0 }
            ? Stopped(module, script.StopReason, in said, own)
            : said;
        module.Failure = $"{failure.Position}: {failure.Text}";

        // A module node that did not compile is a standing problem of its node.
        sink.Tell(script is null && module.Node is { } node ? NodeNamingSink.OnNode(in failure, node) : failure);
    }

    // The host's limits are on the whole call, and the stop lands on what
    // runs then. So a module is to blame for a stop only when half the time
    // limit or more went by in its own code. Otherwise the ask that ran it
    // fails, and the next one runs it again.
    private ScriptMessage Stopped(ScriptModule module, int reason, in ScriptMessage said, TimeSpan own)
    {
        TimeSpan limit = host.CallLimit;
        if (reason == Shim.StopTime && own.Ticks >= limit.Ticks / 2)
            return said;

        module.IsKept = false;
        if (reason != Shim.StopTime)
            return said with { Text = $"{said.Text}. {RunsAgain}" };

        string seconds = limit.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        return said with
        {
            Text = "stopped while it loaded, because the code that asked for it had run for most of the " +
                $"{seconds} s a call may take. {RunsAgain}",
        };
    }

    private static string Sentence(ScriptModule failed) => $"{LoadFailed}: {failed.Failure}";

    private static ScriptMessage Error(ScriptModule module, string text) =>
        new(ScriptSeverity.Error, text, module.Path, 0);
}
