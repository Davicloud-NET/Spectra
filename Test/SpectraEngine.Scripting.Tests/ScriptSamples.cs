using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

// The scripts luau-lsp checks the definitions file against: the samples kept
// in this project's Samples folder, the file Insert Script writes, and the
// demo's own script. Read from the source tree, with line feeds only.
internal static class ScriptSamples
{
    public const string MistakesFolder = "Mistakes";

    // The path the editor's template gets here. In a project it is named after its node.
    public const string TemplatePath = "Scripts/Script.luau";

    // The first script of the guide, and the same with a slip in it.
    public const string FirstSpinPath = "Scripts/first_spin.luau";
    public const string FirstMistakePath = MistakesFolder + "/first_mistake.luau";

    private static readonly Lazy<ScriptSample[]> Loaded = new(Load);

    public static IReadOnlyList<ScriptSample> All => Loaded.Value;

    public static IEnumerable<ScriptSample> Clean => All.Where(sample => !sample.IsMistake);

    public static IEnumerable<ScriptSample> Mistakes => All.Where(sample => sample.IsMistake);

    public static ScriptSample At(string contentPath) =>
        All.Single(sample => sample.ContentPath == contentPath);

    private static ScriptSample[] Load()
    {
        string root = BindingSourceTests.RepositoryRoot();
        string folder = Path.Combine(root, "Test", "SpectraEngine.Scripting.Tests", "Samples", "Assets");

        List<ScriptSample> samples =
        [
            .. Directory.EnumerateFiles(folder, "*.luau", SearchOption.AllDirectories)
                .Select(file => new ScriptSample(Path.GetRelativePath(folder, file).Replace('\\', '/'), Read(file))),
            new(TemplatePath, Read(Path.Combine(root, "SpectraEngine.Editor", "Assets", "Templates", "Script.luau"))),
            new(DemoSpinner.ScriptPath, Read(Path.Combine(root, "Assets", DemoSpinner.ScriptPath))),
        ];

        // EnumerateFiles has no documented order.
        return [.. samples.OrderBy(sample => sample.ContentPath, StringComparer.Ordinal)];
    }

    // A checkout on Windows may hand over carriage returns.
    private static string Read(string file) => File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
}
