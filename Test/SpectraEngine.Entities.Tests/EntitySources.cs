using System;
using System.IO;

namespace SpectraEngine.Entities.Tests;

// The sources of the built-in entities, for the tests that read them as text.
internal static class EntitySources
{
    public static string[] Files()
    {
        string folder = Path.Combine(SolutionRoot(), "SpectraEngine.Entities");
        return [.. Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Where(IsSource)];
    }

    private static bool IsSource(string path)
    {
        char separator = Path.DirectorySeparatorChar;
        return !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            && !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
    }

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not find the solution root above the test binary");
    }
}
