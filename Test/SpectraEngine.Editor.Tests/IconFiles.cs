using System.Text.RegularExpressions;

namespace SpectraEngine.Editor.Tests;

// The icon files on disk and the theme's tokens, read as text. The shell's
// folder holds its own icons and SpectraEngine.Editing's the icons of things
// that stand in the world.
internal static class IconFiles
{
    public static string ShellFolder => Path.Combine(SourceRoot(), "SpectraEngine.Editor", "Assets", "Icons");

    public static string WorldFolder => Path.Combine(SourceRoot(), "SpectraEngine.Editing", "Assets", "Icons");

    public static IEnumerable<string> Folders => [ShellFolder, WorldFolder];

    // Every icon's key, which is its file name without the extension.
    public static HashSet<string> Keys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string folder in Folders)
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.svg"))
                keys.Add(Path.GetFileNameWithoutExtension(file));
        }

        return keys;
    }

    public static HashSet<string> KeysIn(string folder) => new(
        Directory.EnumerateFiles(folder, "*.svg").Select(file => Path.GetFileNameWithoutExtension(file)),
        StringComparer.Ordinal);

    // Every brush the theme defines, by key. The value is its colour as
    // written, or empty when it takes the colour from another resource.
    public static Dictionary<string, string> ThemeBrushes()
    {
        string tokens = File.ReadAllText(
            Path.Combine(SourceRoot(), "SpectraEngine.Editor", "Theme", "Tokens.axaml"));

        var brushes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     tokens, @"<SolidColorBrush x:Key=""(?<key>\w+)"" Color=""(?<color>[^""]*)"""))
        {
            string color = match.Groups["color"].Value;
            brushes[match.Groups["key"].Value] = color.StartsWith('#') ? color : string.Empty;
        }

        return brushes;
    }

    public static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any() || dir.EnumerateFiles("*.sln").Any())
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not find the solution root above the test binary");
    }
}
