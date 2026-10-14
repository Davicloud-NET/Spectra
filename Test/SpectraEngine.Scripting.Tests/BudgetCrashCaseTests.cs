using SpectraEngine.Scripting.CrashCases;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Scripts switched off for what they set on nodes, as native code, run from
/// the published console as the other crash cases are. It skips until the
/// console has been published.
/// </summary>
[Trait("Suite", "CrashCases")]
public sealed class BudgetCrashCaseTests
{
    // A host function that finds the budget gone collects in full and lets
    // loose trees go from inside SetAttribute, AddTag and Clone, fails, and
    // the stop raises again past every pcall.
    [Fact]
    public async Task A_script_of_a_level_is_switched_off_for_what_it_sets_on_nodes_in_a_native_build()
    {
        CrashCaseProcess.Require();

        CrashCaseProcess.Result result = await CrashCaseProcess.Run("budget-in-a-level");

        result.ExitCode.ShouldBe(CaseExit.Passed, result.Output);
        result.Output.ShouldContain("NativeAOT: True");
        result.Output.ShouldContain("stopped 30 scripts at the budget in a level");
    }
}
