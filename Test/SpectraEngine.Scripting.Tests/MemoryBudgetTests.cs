using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The state may hold so many bytes. The script that runs when it holds more
/// is stopped and switched off, and the host goes on.
/// </summary>
public sealed class MemoryBudgetTests
{
    private const long Megabyte = 1024 * 1024;

    [Fact]
    public void A_host_has_a_budget_unless_told_otherwise()
    {
        using LuauHost host = TestHost.Open(out _);

        host.MemoryBudget.ShouldBe(LuauHost.DefaultMemoryBudget);
        host.MemoryUsed.ShouldBeInRange(1, Megabyte);
    }

    [Fact]
    public void A_script_that_holds_too_much_is_stopped_at_its_line_and_switched_off()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        host.MemoryBudget = 8 * Megabyte;
        LuauScript script = host.LoadText("local hoard = {}\nwhile true do\n    hoard[#hoard + 1] = string.rep('x', 1024) .. #hoard\nend", "Scripts/hoarder.luau");

        LuauResume outcome = script.Start();

        outcome.ShouldBe(LuauResume.Failed);
        script.IsSwitchedOff.ShouldBeTrue();
        ScriptMessage message = sink.Single;
        message.Script.ShouldBe("Scripts/hoarder.luau");
        message.Line.ShouldBeInRange(2, 4);
        message.Text.ShouldBe("the scripts hold more than 8 MB and this one was switched off");
        host.MemoryUsed.ShouldBeLessThan(16 * Megabyte);
    }

    [Fact]
    public void The_host_runs_other_scripts_once_the_hoard_is_gone()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        host.MemoryBudget = 8 * Megabyte;
        host.RunText("local hoard = {}\nwhile true do hoard[#hoard + 1] = string.rep('x', 1024) .. #hoard end", "Scripts/hoarder.luau")
            .ShouldBe(LuauResume.Failed);
        sink.Messages.Clear();

        LuauScript other = host.LoadText("local sum = 0\nfor i = 1, 100000 do sum += i end\nreturn sum", "Scripts/other.luau");
        LuauResume outcome = other.Start();

        outcome.ShouldBe(LuauResume.Finished);
        other.IsSwitchedOff.ShouldBeFalse();
        sink.Messages.ShouldBeEmpty();
        host.MemoryUsed.ShouldBeLessThan(8 * Megabyte);
    }

    // What the allocator counts includes garbage. The budget is for what
    // scripts hold, so a table that is over it and already dropped does not
    // stop the script: the check collects before it decides.
    [Fact]
    public void Garbage_does_not_count()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        host.MemoryBudget = 4 * Megabyte;
        LuauScript script = host.LoadText("for round = 1, 20 do\n    local big = table.create(400000, 0)\n    big = nil\nend\nreturn 'done'");

        LuauResume outcome = script.Start();

        outcome.ShouldBe(LuauResume.Finished);
        sink.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void A_script_that_catches_the_stop_is_stopped_all_the_same()
    {
        using LuauHost host = TestHost.Open(out _);
        host.MemoryBudget = 8 * Megabyte;
        LuauScript script = host.LoadText(
            "local hoard = {}\nwhile true do\n    pcall(function() while true do hoard[#hoard + 1] = string.rep('x', 1024) .. #hoard end end)\nend");

        LuauResume outcome = script.Start();

        outcome.ShouldBe(LuauResume.Failed);
        script.IsSwitchedOff.ShouldBeTrue();
    }

    [Fact]
    public void A_budget_of_zero_is_no_budget()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        host.MemoryBudget = 0;
        LuauScript script = host.LoadText("local hoard = {}\nfor i = 1, 20000 do hoard[i] = string.rep('x', 1024) .. i end\nreturn #hoard");

        LuauResume outcome = script.Start();

        outcome.ShouldBe(LuauResume.Finished);
        host.MemoryUsed.ShouldBeGreaterThan(16 * Megabyte);
        sink.Messages.ShouldBeEmpty();
    }
}
