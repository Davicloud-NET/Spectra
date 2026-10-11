using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SpectraEngine.Editor.Tests;

// Runs the shell's icon build on a folder of files. The task is code inside
// Icons.targets, so only MSBuild can run it, in a process of its own.
internal sealed class IconBuild
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private static readonly Regex Geometry = new(@"<PathGeometry x:Key=""(?<key>\w+)"" Figures=""(?<figures>[^""]*)""");

    private IconBuild(int exitCode, string log, Dictionary<string, string> figures)
    {
        ExitCode = exitCode;
        Log = log;
        Figures = figures;
    }

    public int ExitCode { get; }

    // Everything MSBuild printed.
    public string Log { get; }

    // What the build wrote for each icon it accepted, by key.
    public IReadOnlyDictionary<string, string> Figures { get; }

    // The figures in a resource file the build wrote, by key.
    public static Dictionary<string, string> ReadFigures(string resources)
    {
        var figures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Geometry.Matches(resources))
            figures.Add(match.Groups["key"].Value, match.Groups["figures"].Value);

        return figures;
    }

    // A name is a key, or a folder and a key: "World/IconTwin".
    public static async Task<IconBuild> RunAsync(params (string Name, string Svg)[] icons)
    {
        // Skip, not fail: a machine that only runs built tests has a runtime
        // and no SDK, and that is not a broken build.
        (int listed, string sdks) = await DotnetAsync(Path.GetTempPath(), "--list-sdks");
        Assert.SkipWhen(listed != 0 || sdks.Trim().Length == 0, "There is no .NET SDK here to run the icon build with.");

        string folder = Path.Combine(Path.GetTempPath(), "spectra-icon-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            foreach ((string name, string svg) in icons)
            {
                Directory.CreateDirectory(Path.Combine(folder, Path.GetDirectoryName(name) ?? string.Empty));
                File.WriteAllText(Path.Combine(folder, name + ".svg"), svg);
            }

            string output = Path.Combine(folder, "Icons.axaml");
            string project = Path.Combine(folder, "icons.proj");
            File.WriteAllText(project, Project(output));

            // No node reuse: a build node left running would hold the folder.
            (int exitCode, string log) = await DotnetAsync(
                folder, "msbuild", project, "-t:Fixtures", "-nologo", "-nodeReuse:false");
            string resources = File.Exists(output) ? File.ReadAllText(output) : string.Empty;
            return new IconBuild(exitCode, log, ReadFigures(resources));
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A locked temp folder is not a test failure.
            }
        }
    }

    // A project that borrows the task from the shell's own targets file and
    // runs it on the files beside it.
    private static string Project(string output)
    {
        string targets = Path.Combine(IconFiles.SourceRoot(), "SpectraEngine.Editor", "Icons.targets");

        return $"""
            <Project>
              <Import Project="{targets}" />
              <ItemGroup>
                <Fixture Include="$(MSBuildThisFileDirectory)**/*.svg" />
              </ItemGroup>
              <Target Name="Fixtures">
                <GenerateIconResources Icons="@(Fixture)" Output="{output}" />
              </Target>
            </Project>
            """;
    }

    // Runs dotnet and gives back its exit code and everything it printed.
    private static async Task<(int ExitCode, string Output)> DotnetAsync(string folder, params string[] arguments)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = folder,
        };

        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Could not start dotnet.");

        using var patience = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        patience.CancelAfter(Patience);

        // Both pipes at once. Reading one to its end first deadlocks when the
        // child fills the other.
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(patience.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(patience.Token);

        try
        {
            await process.WaitForExitAsync(patience.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await stdout + await stderr);
    }
}
