using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpectraEngine.Scripting.CrashCases;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.Tests;

/// <summary>Stopping a script that will not stop by itself.</summary>
public sealed class InterruptTests
{
    private static readonly ManualResetEventSlim s_loopStarted = new();

    [Fact]
    public async Task A_runaway_script_is_stopped_from_another_thread()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        RegisterLoopStarted(state);

        s_loopStarted.Reset();
        nint running = state.L;
        CancellationToken cancelled = TestContext.Current.CancellationToken;
        Task watchdog = Task.Run(() =>
        {
            s_loopStarted.Wait(cancelled);
            Shim.sl_interrupt_arm(running);
        }, cancelled);

        LuaStatus status = state.Run("loop_started()\nwhile true do end");

        status.ShouldBe(LuaStatus.ErrorRun);
        state.PopText().ShouldBe("test:2: script stopped by the host");
        await watchdog;
    }

    [Fact]
    public void A_stopped_script_cannot_hide_in_a_pcall()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        Shim.sl_interrupt_arm(state.L);

        LuaStatus status = state.Run("while true do\n    pcall(function() while true do end end)\nend");

        status.ShouldBe(LuaStatus.ErrorRun);
        string? message = state.PopText();
        message.ShouldNotBeNull();
        message.ShouldEndWith("script stopped by the host");
    }

    [Fact]
    public void A_disarmed_state_runs_scripts_again()
    {
        NativeLuau.Require();
        using ProbeState state = new();
        Shim.sl_interrupt_arm(state.L);
        state.Run("while true do end").ShouldBe(LuaStatus.ErrorRun);
        Lua.Pop(state.L, 1);

        Shim.sl_interrupt_disarm(state.L);
        LuaStatus status = state.Run("local sum = 0\nfor i = 1, 1000 do sum += i end\nreturn sum", results: 1);

        status.ShouldBe(LuaStatus.Ok);
        state.PopNumber().ShouldBe(500500);
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
        s_loopStarted.Set();
        return 0;
    }
}
