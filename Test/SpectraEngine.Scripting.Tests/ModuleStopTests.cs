namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A module the host stops while it loads. The limits are on the whole
/// call, so a module that was stopped for what its asker used fails that one
/// ask and runs again on the next.
/// </summary>
public sealed class ModuleStopTests
{
    private const string LoadFailed = "Requested module experienced an error while loading";
    private const string RunsAgain = "The module runs again when it is next asked for";

    // The script spends nearly all of the limit, so the module is stopped a
    // moment into its own run. It loops for good here, so that the stop is
    // sure to land in it.
    [Fact]
    public void A_module_stopped_for_the_time_its_asker_spent_fails_that_ask_and_runs_again_on_the_next()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/util.luau", "local turns = 0\nwhile true do turns += 1 end");
        level.ScriptAt("Scripts/main.luau", """
            local started = os.clock()
            while os.clock() - started < 0.77 do end
            print(pcall(require, './util'))
            task.wait()
            print(pcall(require, './util'))
            """);

        level.Play();
        level.Runtime.ShouldNotBeNull().CallLimit = TimeSpan.FromMilliseconds(800);
        level.Tick();
        level.FileAt("Scripts/util.luau", "print('ran again')\nreturn 'util'");
        level.Tick();

        const string Stopped =
            "Scripts/util.luau:2: stopped while it loaded, because the code that asked for it had run for most " +
            $"of the 0.8 s a call may take. {RunsAgain}";
        level.ErrorsWithPlace().ShouldBe([Stopped]);
        level.Printed.ShouldBe([$"false Scripts/main.luau:3: {LoadFailed}: {Stopped}", "ran again", "true util"]);
    }

    // The script holds most of the budget and the module takes the rest.
    [Fact]
    public void A_module_stopped_for_memory_fails_that_ask_and_runs_again_on_the_next()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/util.luau", Hungry);
        level.ScriptAt("Scripts/main.luau", """
            local ballast = string.rep('x', 48 * 1024 * 1024)
            print(pcall(require, './util'))
            ballast = nil
            print(pcall(require, './util'))
            """);

        level.Play();
        level.Runtime.ShouldNotBeNull().MemoryBudget = 64 * 1024 * 1024;
        level.Tick();

        level.ErrorsWithPlace().ShouldBe([StoppedForMemory]);
        level.Printed.ShouldBe([$"false Scripts/main.luau:2: {LoadFailed}: {StoppedForMemory}", "true 24576"]);
    }

    // The module in between did nothing wrong either, so it is run again too.
    [Fact]
    public void A_module_that_failed_for_one_that_runs_again_runs_again_itself()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/util.luau", Hungry);
        level.FileAt("Scripts/outer.luau", "print('outer runs')\nreturn require('./util') + 1");
        level.ScriptAt("Scripts/main.luau", """
            local ballast = string.rep('x', 48 * 1024 * 1024)
            print(pcall(require, './outer'))
            ballast = nil
            print(pcall(require, './outer'))
            """);

        level.Play();
        level.Runtime.ShouldNotBeNull().MemoryBudget = 64 * 1024 * 1024;
        level.Tick();

        level.ErrorsWithPlace().ShouldBe([StoppedForMemory]);
        level.Printed.ShouldBe(
        [
            "outer runs",
            $"false Scripts/main.luau:2: {LoadFailed}: {StoppedForMemory}",
            "outer runs",
            "true 24577",
        ]);
    }

    // The loop is where Luau lets the host look at the memory.
    private const string Hungry = """
        local own = string.rep('y', 24 * 1024 * 1024)
        local held = 0
        for i = 1, #own / 1024 do held += 1 end
        return held
        """;

    private const string StoppedForMemory =
        $"Scripts/util.luau:3: the scripts hold more than 64 MB and this one was switched off. {RunsAgain}";
}
