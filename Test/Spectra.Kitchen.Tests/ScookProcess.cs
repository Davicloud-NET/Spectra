using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Spectra.Kitchen.Tests;

// Runs the real scook binary in its own process. ScookCliTests.Invoke is the
// fast in-process path; this one is for tests that need a fresh process, such
// as the determinism oracles (the string hash seed is per process).
internal static class ScookProcess
{
    // Null when scook has not been built beside the tests.
    public static string? BinaryPath { get; } = Locate();

    // Skip, not fail: a missing build output is not a broken cook.
    public static void Require() =>
        Assert.SkipWhen(
            BinaryPath is null,
            "scook is not beside the test binary. It is build output of Spectra.Kitchen.CLI, " +
            "which this project references - build it with: dotnet build");

    public static Result Run(params string[] args)
    {
        string path = BinaryPath
            ?? throw new InvalidOperationException("scook was not found; call Require() first.");

        var info = new ProcessStartInfo(path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // Fixed, so a run does not depend on where the test host started.
            WorkingDirectory = AppContext.BaseDirectory,
        };

        // --no-color first: appended, a trailing option swallows it as its argument.
        info.ArgumentList.Add("--no-color");
        foreach (string arg in args) info.ArgumentList.Add(arg);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"Could not start '{path}'.");

        // Drain both pipes at once. Reading one to the end first deadlocks when
        // the child fills the other.
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

        process.WaitForExit();

        return new Result(process.ExitCode, stdout.Result, stderr.Result);
    }

    public readonly record struct Result(int ExitCode, string Stdout, string Stderr);

    private static string? Locate()
    {
        string name = OperatingSystem.IsWindows() ? "scook.exe" : "scook";
        string path = Path.Combine(AppContext.BaseDirectory, name);
        return File.Exists(path) ? path : null;
    }
}
