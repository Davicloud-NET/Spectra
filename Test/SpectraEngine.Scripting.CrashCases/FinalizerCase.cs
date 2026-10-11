using System.Text;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.CrashCases;

// Luau runs the finalizers of a coroutine by recursion on the native stack,
// and its limit on C calls does not count that. Enough of them on one
// coroutine overflow the stack, where no error handler and no watchdog gets
// a turn. Each script here piles them up and has them run another way.
internal static class FinalizerCase
{
    private const string Count = "200000";

    private const string ClosedByTheScript = $"""
        local co = coroutine.create(function() coroutine.yield() end)
        coroutine.resume(co)
        for i = 1, {Count} do coroutine.finally(co, type) end
        return coroutine.close(co)
        """;

    private const string EndedByAResume = $"""
        local co = coroutine.create(function() coroutine.yield() end)
        coroutine.resume(co)
        for i = 1, {Count} do coroutine.finally(co, type) end
        return coroutine.resume(co)
        """;

    // Where a script cannot yield, a finalizer written in Luau recurses too.
    private const string ClosedInAMetamethod = $$"""
        local co = coroutine.create(function() coroutine.yield() end)
        coroutine.resume(co)
        for i = 1, {{Count}} do coroutine.finally(co, function() end) end
        local closer = setmetatable({}, { __index = function() return coroutine.close(co) end })
        return closer.now
        """;

    // The host ends this one while it waits.
    private const string LeftWaiting = $"""
        local me = coroutine.running()
        for i = 1, {Count} do coroutine.finally(me, function() end) end
        coroutine.yield()
        """;

    private static readonly (string Name, string Source)[] _scripts =
    [
        ("closed by the script", ClosedByTheScript),
        ("ended by a resume", EndedByAResume),
        ("closed in a metamethod", ClosedInAMetamethod),
        ("left waiting and ended by the host", LeftWaiting),
    ];

    internal static int Run()
    {
        using LuauHost host = new(new CountingSink());
        host.Seal();

        foreach ((string name, string source) in _scripts)
        {
            Console.WriteLine($"step: finalizers on a coroutine that is {name}");
            if (Play(host, source) is { } failure)
            {
                Console.WriteLine($"CASE failed: {failure}");
                return CaseExit.Failed;
            }
        }

        LuauScript? last = host.Load("after.luau", "return 6 * 7"u8);
        bool isRunning = last is not null
            && last.Start() == LuauResume.Finished
            && last.Thread.Values.TryGetNumber(1, out double answer)
            && answer == 42;
        if (!isRunning)
        {
            Console.WriteLine("CASE failed: the script after the finalizers did not run");
            return CaseExit.Failed;
        }

        Console.WriteLine($"CASE went on after {_scripts.Length} scripts that pile up finalizers");
        return CaseExit.Passed;
    }

    // Null when the process is still here. A script may fail: a Luau with no
    // finalizers raises an error at the first one.
    private static string? Play(LuauHost host, string source)
    {
        LuauScript? script = host.Load("finalizers.luau", Encoding.UTF8.GetBytes(source));
        if (script is null)
            return "a script did not compile";

        LuauResume outcome = script.Start();
        if (outcome == LuauResume.Yielded)
            script.Thread.Cancel();

        host.Collect();
        return outcome == LuauResume.Refused ? "a script was refused" : null;
    }
}
