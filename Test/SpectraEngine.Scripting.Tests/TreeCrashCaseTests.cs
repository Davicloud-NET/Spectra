using SpectraEngine.Scripting.CrashCases;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Making, cloning, reparenting and destroying nodes as native code, run
/// from the published console as the other crash cases are. It skips until
/// the console has been published.
/// </summary>
[Trait("Suite", "CrashCases")]
public sealed class TreeCrashCaseTests
{
    // Every refusal raised out of a host function, threads and connections
    // ended from inside one, a clone's script started from the tick, a full
    // collection run from inside Instance.new, and Stop putting the tree back.
    [Fact]
    public async Task A_script_of_a_level_makes_clones_moves_and_destroys_nodes_in_a_native_build()
    {
        CrashCaseProcess.Require();

        CrashCaseProcess.Result result = await CrashCaseProcess.Run("nodes-in-a-level");

        result.ExitCode.ShouldBe(CaseExit.Passed, result.Output);
        result.Output.ShouldContain("NativeAOT: True");
        result.Output.ShouldContain("made, cloned and destroyed 580 times in a level, 320 refused");
    }
}
