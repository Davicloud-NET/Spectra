using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using LuauSpike.Checks;

namespace LuauSpike;

// Usage:
//   LuauSpike                         run every check
//   LuauSpike --scripts=<dir>         read scripts from another folder
//   LuauSpike --emit-bytecode=<dir>   compile scripts/*.luau to <dir>/*.luauc
//   LuauSpike --case=<name>           run one crash case and exit (used by check 4)
internal static unsafe class Program
{
    internal static string LinkFlavor
    {
        get
        {
#if LUAU_SHARED
            return "shared library";
#elif LUAU_CODEGEN
            return "static: VM, compiler, code generator";
#elif LUAU_COMPILER
            return "static: VM, compiler";
#else
            return "static: VM only";
#endif
        }
    }

    private static int Main(string[] args)
    {
        string? crashCase = null;
        string? emitFolder = null;
        foreach (string arg in args)
        {
            if (arg.StartsWith("--case=", StringComparison.Ordinal))
                crashCase = arg["--case=".Length..];
            else if (arg.StartsWith("--scripts=", StringComparison.Ordinal))
                Scripts.Folder = arg["--scripts=".Length..];
            else if (arg.StartsWith("--emit-bytecode=", StringComparison.Ordinal))
                emitFolder = arg["--emit-bytecode=".Length..];
        }

        if (crashCase is not null)
            return CrashCases.Run(crashCase);
        if (emitFolder is not null)
            return EmitBytecode(emitFolder);

        Report report = new();
        string pinning = PinForTiming();
        PrintHeader();
        Console.WriteLine($"Timing: {pinning}; best of {Bench.Rounds} rounds unless a line says otherwise");
        Console.WriteLine();
        CheckAbi(report);

        BasicChecks.Run(report);
        ErrorChecks.Run(report);
        UserdataChecks.Run(report);
        CoroutineChecks.Run(report);
        InterruptChecks.Run(report);
        SandboxChecks.Run(report);
        CostChecks.Run(report);
        CodeGenChecks.Run(report);

        Console.WriteLine(report.Failures == 0 ? "ALL CHECKS PASSED" : $"{report.Failures} CHECK(S) FAILED");
        return report.Failures == 0 ? 0 : 1;
    }

    // A NativeAOT executable has no assembly file. The feature switch alone
    // proves nothing: PublishAot turns it off for a JIT run too.
    internal static bool IsNativeAot
    {
        [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty Location is the signal.")]
        get => !RuntimeFeature.IsDynamicCodeSupported && typeof(Program).Assembly.Location.Length == 0;
    }

    // Two logical CPUs: one for the script, one for the watchdog in check 7.
    // On a hybrid CPU this also keeps the run off the slow cores.
    private static string PinForTiming()
    {
        try
        {
            Process self = Process.GetCurrentProcess();
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                self.ProcessorAffinity = 0b101;
            if (OperatingSystem.IsWindows())
                self.PriorityClass = ProcessPriorityClass.High;
            return OperatingSystem.IsWindows() ? "pinned to logical CPUs 0 and 2, high priority" : "pinned to logical CPUs 0 and 2";
        }
        catch (Exception e) // boundary: timing still works unpinned, just noisier
        {
            return "not pinned (" + e.Message + ")";
        }
    }

    private static void PrintHeader()
    {
        string unwind = Shim.sl_unwind_mode() != 0 ? "longjmp" : "C++ exceptions";
        string compiler = Marshal.PtrToStringUTF8((nint)Shim.sl_compiler()) ?? "?";
        Console.WriteLine($"Luau spike on {RuntimeInformation.RuntimeIdentifier}, {RuntimeInformation.OSDescription}");
        Console.WriteLine($"{RuntimeInformation.FrameworkDescription}, NativeAOT: {IsNativeAot}");
        Console.WriteLine($"Luau linked as: {LinkFlavor}; errors raised with {unwind}; Luau built with {compiler}");
    }

    // The constants in Lua.cs are copied from Luau's headers. A release that
    // moves one would otherwise read the wrong stack slot or misname a type.
    private static void CheckAbi(Report report)
    {
        ReadOnlySpan<int> expected =
        [
            Lua.RegistryIndex, Lua.GlobalsIndex, Lua.TNumber, Lua.TString, Lua.TTable, Lua.TFunction, Lua.TUserdata, Lua.UserdataTagLimit,
        ];

        StringBuilder mismatches = new();
        for (int i = 0; i < expected.Length; i++)
        {
            int actual = Shim.sl_abi_value(i);
            if (actual != expected[i])
                mismatches.Append($" value {i}: binding {expected[i]}, library {actual};");
        }

        report.Check("0", "binding constants match the library", mismatches.Length == 0,
            mismatches.Length == 0 ? $"{expected.Length} constants compared" : mismatches.ToString());
    }

    private static int EmitBytecode(string folder)
    {
#if LUAU_COMPILER
        Directory.CreateDirectory(folder);
        foreach (string path in Directory.GetFiles(Scripts.Folder, "*.luau"))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            byte[] bytecode = Scripts.Compile(File.ReadAllBytes(path));
            File.WriteAllBytes(Path.Combine(folder, name + ".luauc"), bytecode);
            Console.WriteLine($"{name}.luauc  {bytecode.Length} B");
        }
        return 0;
#else
        Console.WriteLine("This build has no compiler.");
        return 2;
#endif
    }
}
