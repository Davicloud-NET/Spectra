using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SpectraEngine.Scripting.Luau.Native;

namespace SpectraEngine.Scripting.CrashCases;

// A state is made and used on one thread and closed from another. The editor
// does that when its engine thread has died and it starts a new session.
internal static unsafe class CloseCase
{
    private const int Rounds = 50;

    internal static int Run()
    {
        for (int round = 1; round <= Rounds; round++)
        {
            ProbeState? state = null;
            string? failure = "the thread did not run";
            Thread user = new(() =>
            {
                state = new ProbeState();
                failure = UseState(state);
            });
            user.Start();
            user.Join();

            if (state is null || failure is not null)
            {
                Console.WriteLine($"CASE failed in round {round}: {failure}");
                return CaseExit.Failed;
            }

            GC.Collect();
            state.Dispose();
        }

        using ProbeState last = new();
        if (last.Run("return probe('ab', 2)", results: 2) != LuaStatus.Ok || last.PopNumber() != 4)
        {
            Console.WriteLine("CASE failed: a state opened after the rounds does not run a script");
            return CaseExit.Failed;
        }

        Console.WriteLine($"CASE closed {Rounds} states from another thread");
        return CaseExit.Passed;
    }

    // Leaves behind what a level leaves: a script that ran, a thread that
    // waits and will never be resumed, and garbage. Null when all of it worked.
    private static string? UseState(ProbeState state)
    {
        nint L = state.L;
        if (state.Run("return probe('ab', 3)", results: 2) != LuaStatus.Ok)
            return "the script failed: " + state.PopText();
        if (state.PopNumber() != 6 || state.PopText() != "ababab")
            return "the probe returned something else";

        fixed (byte* name = "pause"u8)
            state.Require(Shim.sl_pushhostfunction(L, &Pause, name));
        state.Require(state.SetGlobal("pause"u8));

        nint waiting;
        state.Require(Shim.sl_newthread(L, &waiting));
        LuaStatus loaded = LuaChunk.Load(waiting, "=waiting", Encoding.UTF8.GetBytes("pause() return 1"));
        if (loaded != LuaStatus.Ok)
            return "the waiting script did not load";

        LuaStatus resumed = state.Resume(waiting);
        return resumed == LuaStatus.Yield ? null : $"the waiting script ended with {resumed}";
    }

    // Yields the script that called it, with no values.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Pause(nint L) => Shim.HostYield;
}
