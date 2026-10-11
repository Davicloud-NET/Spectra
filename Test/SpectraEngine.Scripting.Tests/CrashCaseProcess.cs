using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Scripting.Tests;

// Runs one case of the published crash-case console in a process of its own.
// Only the NativeAOT publish will do: under the JIT the case that must die
// survives, so the binary a build leaves in bin is of no use here.
internal static class CrashCaseProcess
{
    private const string Project = "SpectraEngine.Scripting.CrashCases";

    private static readonly string RuntimeId =
        (OperatingSystem.IsWindows() ? "win-" : "linux-") + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    // Null when the console has not been published.
    private static string? BinaryPath { get; } = Locate();

    internal static void Require() =>
        Assert.SkipWhen(
            BinaryPath is null,
            "The crash cases are not published. They only mean something as a native executable. " +
            $"Publish them with: dotnet publish Test/{Project} -c Release -r {RuntimeId}");

    internal static async Task<Result> Run(string caseName)
    {
        string path = BinaryPath
            ?? throw new InvalidOperationException("The crash cases were not found. Call Require() first.");

        ProcessStartInfo info = new(path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path),
        };
        info.ArgumentList.Add("--case=" + caseName);

        using Process process = Process.Start(info)
            ?? throw new InvalidOperationException($"Could not start '{path}'.");

        // Both pipes at once. Reading one to its end first deadlocks when the
        // child fills the other.
        CancellationToken cancelled = TestContext.Current.CancellationToken;
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancelled);
        Task<string> error = process.StandardError.ReadToEndAsync(cancelled);
        await process.WaitForExitAsync(cancelled);

        return new Result(process.ExitCode, await output + await error);
    }

    private static string? Locate()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Spectra.slnx")))
            folder = folder.Parent;
        if (folder is null)
            return null;

        string name = OperatingSystem.IsWindows() ? Project + ".exe" : Project;
        string path = Path.Combine(folder.FullName, "Test", Project, "bin", "Release", "net10.0", RuntimeId, "publish", name);
        return File.Exists(path) ? path : null;
    }

    internal readonly record struct Result(int ExitCode, string Output);
}
