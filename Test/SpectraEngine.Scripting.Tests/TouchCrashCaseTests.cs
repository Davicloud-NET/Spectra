using SpectraEngine.Scripting.CrashCases;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Touch as native code, run from the published console as the other crash
/// cases are. It skips until the console has been published.
/// </summary>
[Trait("Suite", "CrashCases")]
public sealed class TouchCrashCaseTests
{
    // The touch pass, the mover, the listener's call from the queue and the
    // respawn, with a refusal raised out of a host function between them.
    [Fact]
    public async Task A_kill_brick_sends_the_player_back_and_a_world_brush_is_refused_in_a_native_build()
    {
        CrashCaseProcess.Require();

        CrashCaseProcess.Result result = await CrashCaseProcess.Run("touch-in-a-level");

        result.ExitCode.ShouldBe(CaseExit.Passed, result.Output);
        result.Output.ShouldContain("NativeAOT: True");
        result.Output.ShouldContain("walked onto a kill brick in 10 levels, 240 refused");
    }
}
