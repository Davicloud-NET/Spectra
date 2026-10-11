using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpectraEngine.Scripting.CrashCases;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Tests;

/// <summary>Stopping a script that will not stop by itself.</summary>
public sealed class InterruptTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private static readonly ManualResetEventSlim _loopStarted = new();

    [Fact]
    public async Task A_runaway_script_is_stopped_from_another_thread()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        RegisterLoopStarted(state);

        _loopStarted.Reset();
        nint running = state.L;
        CancellationToken cancelled = TestContext.Current.CancellationToken;
        Task watchdog = Task.Run(() =>
        {
            if (_loopStarted.Wait(Deadline, cancelled))
                Shim.sl_interrupt_arm(running);
        }, cancelled);

        LuaStatus status = await RunToTheDeadline(state, "loop_started()\nwhile true do end");

        status.ShouldBe(LuaStatus.ErrorRun);
        state.PopText().ShouldBe("test:2: script stopped by the host");
        await watchdog;
    }

    [Fact]
    public async Task A_stopped_script_cannot_hide_in_a_pcall()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        Shim.sl_interrupt_arm(state.L);

        LuaStatus status = await RunToTheDeadline(state, "while true do\n    pcall(function() while true do end end)\nend");

        status.ShouldBe(LuaStatus.ErrorRun);
        string? message = state.PopText();
        message.ShouldNotBeNull();
        message.ShouldEndWith("script stopped by the host");
    }

    [Fact]
    public async Task A_disarmed_state_runs_scripts_again()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        Shim.sl_interrupt_arm(state.L);
        (await RunToTheDeadline(state, "while true do end")).ShouldBe(LuaStatus.ErrorRun);
        Lua.Pop(state.L, 1);

        Shim.sl_interrupt_disarm(state.L);
        LuaStatus status = state.Run("local sum = 0\nfor i = 1, 1000 do sum += i end\nreturn sum", results: 1);

        status.ShouldBe(LuaStatus.Ok);
        state.PopNumber().ShouldBe(500500);
    }

    // Runs an endless script on a thread of its own. When the interrupt does
    // not stop it, the test fails at the deadline and the suite goes on. The
    // state is then left open, because that thread still runs it.
    private static async Task<LuaStatus> RunToTheDeadline(ProbeState state, string source)
    {
        Task<LuaStatus> run = Task.Factory.StartNew(
            () => state.Run(source),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        try
        {
            return await run.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"The interrupt did not stop the script within {Deadline.TotalSeconds:0} s.");
        }
        finally
        {
            if (!run.IsCompleted)
                state.Abandon();
        }
    }

    private static unsafe void RegisterLoopStarted(ProbeState state)
    {
        fixed (byte* name = "loop_started"u8)
            state.Require(Shim.sl_pushhostfunction(state.L, &LoopStarted, name));
        state.Require(state.SetGlobal("loop_started"u8));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int LoopStarted(nint L)
    {
        _loopStarted.Set();
        return 0;
    }
}
