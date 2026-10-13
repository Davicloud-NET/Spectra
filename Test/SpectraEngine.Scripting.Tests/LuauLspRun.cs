namespace SpectraEngine.Scripting.Tests;

// How one run of "luau-lsp analyze" ended: its exit code, what it said of
// the scripts, and everything it printed. A run that could not read a script
// names no line of it, so only the exit code tells it from a clean run.
internal sealed record LuauLspRun(int ExitCode, LuauLspDiagnostic[] Diagnostics, string Said);
