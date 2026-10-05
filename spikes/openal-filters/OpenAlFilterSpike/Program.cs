using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenAlFilterSpike.Checks;

namespace OpenAlFilterSpike;

// Runs every check and prints PASS or FAIL with numbers. Nothing is played:
// every sound is rendered into memory through a loopback device.
internal static class Program
{
    private static int Main()
    {
        Report report = new();
        report.Info("NativeAOT", IsNativeAot.ToString());
        report.Info("Runtime", $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.RuntimeIdentifier}");
        report.Info("System", $"{RuntimeInformation.OSDescription}, {Environment.ProcessorCount} logical CPUs");

        OpenAl lib;
        try
        {
            lib = OpenAl.LoadSoft();
        }
        catch (Exception e) // boundary: no library means nothing to check
        {
            Console.WriteLine($"FAIL  the OpenAL runtime could not be loaded ({e.GetType().Name}: {e.Message})");
            return 1;
        }

        using (lib)
        {
            Console.WriteLine();
            report.Run("1", "filters through function pointers", () => EfxChecks.Run(report, lib));
            report.Run("2", "the loopback device", () => LoopbackChecks.Run(report, lib));
            report.Run("3", "what the filter does", () => ResponseChecks.Run(report, lib));
            report.Run("4", "changes while a sound plays", () => LiveChangeChecks.Run(report, lib));
            report.Run("4", "what a change does to the low end", () => StepJumpChecks.Run(report, lib));
            report.Run("5", "reuse", () => ReuseChecks.Run(report, lib));
            report.Run("6", "a library with no filters", () => MissingChecks.Run(report, lib));
            report.Run("8", "cost", () => CostChecks.Run(report, lib));
            report.Run("1", "no context", () => EfxChecks.RunWithoutContext(report, lib));
        }

        Console.WriteLine(report.Failures == 0 ? "ALL CHECKS PASSED" : $"{report.Failures} CHECK(S) FAILED");
        return report.Failures == 0 ? 0 : 1;
    }

    // A NativeAOT executable has no assembly file. The feature switch alone
    // proves nothing: PublishAot turns it off for a JIT run too.
    private static bool IsNativeAot
    {
        [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty Location is the signal.")]
        get => !RuntimeFeature.IsDynamicCodeSupported && typeof(Program).Assembly.Location.Length == 0;
    }
}
