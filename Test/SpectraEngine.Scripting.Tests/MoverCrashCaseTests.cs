using SpectraEngine.Scripting.CrashCases;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The mover in Luau as native code, run from the published console as the
/// other crash cases are. It skips until the console has been published.
/// </summary>
[Trait("Suite", "CrashCases")]
public sealed class MoverCrashCaseTests
{
    // The thread the host keeps and resumes on every tick, a refusal raised
    // out of a host function inside it, and each way a mover goes wrong:
    // an error, a wait, a value handed back, a loop the host stops, and a
    // level closed from another thread while the mover's call runs.
    [Fact]
    public async Task A_mover_moves_the_player_goes_wrong_and_the_built_in_one_takes_over_in_a_native_build()
    {
        CrashCaseProcess.Require();

        CrashCaseProcess.Result result = await CrashCaseProcess.Run("mover-in-a-level");

        result.ExitCode.ShouldBe(CaseExit.Passed, result.Output);
        result.Output.ShouldContain("NativeAOT: True");
        result.Output.ShouldContain("moved the player by a mover in Luau in 16 levels, and the built-in mover took over in each");
    }
}
