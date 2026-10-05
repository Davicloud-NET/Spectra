namespace LuauSpike.Checks;

// Check 6: a script that yields and is resumed later from C#.
internal static unsafe class CoroutineChecks
{
    private const int TickerResumes = 1_000_000;

    internal static void Run(Report report)
    {
        nint L = LuaHost.NewState();
        try
        {
            int module = Scripts.LoadModule(L, "coroutine");
            int basic = Scripts.LoadModule(L, "basic");
            report.Run("6", "yield and resume from C#", () => YieldAndResume(report, L, module, basic));
            report.Run("6", "resume cost", () => ResumeCost(report, L, module));
        }
        finally
        {
            Shim.sl_close(L);
        }
    }

    private static void YieldAndResume(Report report, nint L, int module, int basic)
    {
        // The thread stays on the main stack so the collector sees it.
        nint thread = Lua.lua_newthread(L);
        LuaStack.PushMember(L, module, "run"u8);
        Lua.lua_xmove(L, thread, 1);

        Lua.lua_pushnumber(thread, 10);
        int firstStatus = Lua.lua_resume(thread, 0, 1);
        double firstYield = Lua.ToNumber(thread, -1);
        Lua.lua_settop(thread, 0);

        // Other work on the main thread between resumes, as a frame would do.
        double between = OtherWork(L, basic);

        Lua.lua_pushnumber(thread, 100);
        int secondStatus = Lua.lua_resume(thread, 0, 1);
        double secondYield = Lua.ToNumber(thread, -1);
        Lua.lua_settop(thread, 0);

        between += OtherWork(L, basic);

        Lua.lua_pushnumber(thread, 1000);
        int finalStatus = Lua.lua_resume(thread, 0, 1);
        double result = Lua.ToNumber(thread, -1);
        Lua.Pop(L, 1);

        bool passed = firstStatus == Lua.Yield && firstYield == 11
            && secondStatus == Lua.Yield && secondYield == 0.5
            && finalStatus == Lua.Ok && result == 1110 && between == 84;
        report.Check("6", "yield and resume from C#", passed,
            $"coroutine.yield gave {firstYield}; the host function wait(0.5) yielded {secondYield}; the script returned {result} after two resumes");
    }

    private static double OtherWork(nint L, int basic)
    {
        LuaStack.PushMember(L, basic, "add"u8);
        Lua.lua_pushnumber(L, 2);
        Lua.lua_pushnumber(L, 40);
        LuaStack.Call(L, 2, 1);
        double sum = Lua.ToNumber(L, -1);
        Lua.Pop(L, 1);
        return sum;
    }

    private static void ResumeCost(Report report, nint L, int module)
    {
        nint thread = Lua.lua_newthread(L);
        LuaStack.PushMember(L, module, "ticker"u8);
        Lua.lua_xmove(L, thread, 1);

        int status = Lua.lua_resume(thread, 0, 0);
        double nanoseconds = Bench.BestNanosecondsPerOperation(Bench.Rounds, TickerResumes, () => status = ResumeLoop(thread, TickerResumes));
        Lua.Pop(L, 1);

        report.Note("6", "resume and yield again", $"{nanoseconds:F1} ns per resume (status {status})");
    }

    private static int ResumeLoop(nint thread, int resumes)
    {
        int status = 0;
        for (int i = 0; i < resumes; i++)
            status = Lua.lua_resume(thread, 0, 0);
        return status;
    }
}
