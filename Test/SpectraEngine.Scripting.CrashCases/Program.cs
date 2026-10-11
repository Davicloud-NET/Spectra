using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.CrashCases;

// Runs one case and exits with a CaseExit code, or dies trying. Each case
// class says what it shows.
internal static partial class Program
{
    private const string Cases =
        "call-host-function, compile-error, raise-through-shim, raise-from-managed-frame or close-from-another-thread";

    // A native executable has no assembly file. The dynamic code switch alone
    // says nothing: PublishAot turns it off for a JIT run too.
    private static bool IsNativeAot
    {
        [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty Location is the signal.")]
        get => !RuntimeFeature.IsDynamicCodeSupported && typeof(Program).Assembly.Location.Length == 0;
    }

    private static int Main(string[] args)
    {
        string? name = null;
        foreach (string arg in args)
        {
            if (arg.StartsWith("--case=", StringComparison.Ordinal))
                name = arg["--case=".Length..];
        }

        // A case that dies must end the process, not open a dialog and wait.
        if (OperatingSystem.IsWindows())
            SetErrorMode(FailCriticalErrors | NoGpFaultErrorBox | NoOpenFileErrorBox);

        Console.WriteLine($"NativeAOT: {IsNativeAot}");
        if (LuauLibrary.FindProblem() is { } problem)
        {
            Console.WriteLine(problem);
            return CaseExit.LibraryProblem;
        }

        switch (name)
        {
            case "call-host-function":
                return CallCase.Run();
            case "compile-error":
                return CompileCase.Run();
            case "raise-through-shim":
                return RaiseCase.Run(throughShim: true);
            case "raise-from-managed-frame":
                return RaiseCase.Run(throughShim: false);
            case "close-from-another-thread":
                return CloseCase.Run();
            default:
                Console.WriteLine($"Give one case: --case={Cases}.");
                return CaseExit.UnknownCase;
        }
    }

    private const uint FailCriticalErrors = 0x0001;
    private const uint NoGpFaultErrorBox = 0x0002;
    private const uint NoOpenFileErrorBox = 0x8000;

    [LibraryImport("kernel32")]
    private static partial uint SetErrorMode(uint mode);
}
