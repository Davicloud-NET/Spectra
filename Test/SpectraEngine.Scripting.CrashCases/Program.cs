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
        "call-host-function, compile-error, raise-through-shim, raise-from-managed-frame, " +
        "close-from-another-thread, host-stress, yield-from-host, many-finalizers, run-inside-host-call, " +
        "require-in-a-level, inputs-and-outputs-in-a-level, members-in-a-level, touch-in-a-level, tags-in-a-level, " +
        "nodes-in-a-level or mover-in-a-level";

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
            case "host-stress":
                return HostStressCase.Run();
            case "yield-from-host":
                return YieldCase.Run();
            case "many-finalizers":
                return FinalizerCase.Run();
            case "run-inside-host-call":
                return NestedCase.Run();
            case "require-in-a-level":
                return RequireCase.Run();
            case "inputs-and-outputs-in-a-level":
                return WiringCase.Run();
            case "members-in-a-level":
                return MemberCase.Run();
            case "touch-in-a-level":
                return TouchCase.Run();
            case "tags-in-a-level":
                return TagCase.Run();
            case "nodes-in-a-level":
                return TreeCase.Run();
            case "mover-in-a-level":
                return MoverCase.Run();
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
