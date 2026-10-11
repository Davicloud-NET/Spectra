using SpectraEngine.Scripting.CrashCases;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The cases that only a native build can decide, run from the published
/// console. They skip until it has been published.
/// </summary>
// CI runs this group by its trait, in the jobs that publish the console.
[Trait("Suite", "CrashCases")]
public sealed class CrashCaseTests
{
    [Fact]
    public async Task A_script_calls_the_host_function_in_a_native_build()
    {
        CrashCaseProcess.Require();

        CrashCaseProcess.Result result = await CrashCaseProcess.Run("call-host-function");

        result.ExitCode.ShouldBe(CaseExit.Passed, result.Output);
        result.Output.ShouldContain("NativeAOT: True");
    }

    [Fact]
    public async Task The_compiler_refuses_scripts_in_a_native_build()
    {
        CrashCaseProcess.Require();

        CrashCaseProcess.Result result = await CrashCaseProcess.Run("compile-error");

        result.ExitCode.ShouldBe(CaseExit.Passed, result.Output);
    }

    [Fact]
    public async Task An_error_raised_through_the_shim_survives_garbage_collection()
    {
        CrashCaseProcess.Require();

        CrashCaseProcess.Result result = await CrashCaseProcess.Run("raise-through-shim");

        result.ExitCode.ShouldBe(CaseExit.Passed, result.Output);
        result.Output.ShouldContain("finally ran 200 times");
    }

    // The reason for the shim. What happens is undefined, and each of these
    // counts: the process dies, hangs, or goes on and finds itself broken. A
    // clean run fails the test, because the right way then proves nothing:
    // the wrong way gets through as well.
    [Fact]
    public async Task A_raise_from_a_managed_frame_breaks_a_native_build()
    {
        CrashCaseProcess.Require();

        CrashCaseProcess.Result result = await CrashCaseProcess.Run("raise-from-managed-frame");

        result.Output.ShouldContain("NativeAOT: True");
        result.Output.ShouldContain("step: calling lua_error from a managed frame");
        (result.WasKilled || CaseExit.ShowsTheBreach(result.ExitCode)).ShouldBeTrue(
            $"the process got through a raise over a managed frame and ended with {result.ExitCode}:\n{result.Output}");
    }

    [Fact]
    public async Task A_state_used_on_one_thread_is_closed_from_another()
    {
        CrashCaseProcess.Require();

        CrashCaseProcess.Result result = await CrashCaseProcess.Run("close-from-another-thread");

        result.ExitCode.ShouldBe(CaseExit.Passed, result.Output);
    }
}
