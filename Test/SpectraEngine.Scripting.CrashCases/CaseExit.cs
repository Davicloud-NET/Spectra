namespace SpectraEngine.Scripting.CrashCases;

// What the console exits with when it ends by itself. A case that dies exits
// with whatever the system gives a killed process, which is none of these.
internal static class CaseExit
{
    internal const int Passed = 0;
    internal const int Failed = 1;
    internal const int UnknownCase = 2;
    internal const int LibraryProblem = 3;

    // The raise over a managed frame went unpunished, as it does under the JIT.
    internal const int SurvivedTheWrongWay = 4;

    // The raise over a managed frame did not kill the process, and a later
    // round then went wrong or found an object changed that nothing wrote to.
    internal const int BrokenByTheWrongWay = 5;

    // How the wrong way ends in a native build: killed, or caught broken.
    internal static bool ShowsTheBreach(int exitCode) =>
        exitCode == BrokenByTheWrongWay || exitCode is < Passed or > BrokenByTheWrongWay;
}
