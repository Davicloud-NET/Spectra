namespace SpectraEngine.Editor.Tests;

/// <summary>
/// A dock tool's content and its DataContext must be assigned together.
/// </summary>
// Dock gives tool content its own DataContext, and Avalonia reports nothing for
// a failed binding: Text goes empty and IsVisible stays true. This project has
// no Avalonia, so the rule is checked against the sources.
public sealed class ToolContentConventionTests
{
    [Fact]
    public void Every_dock_tool_takes_its_content_through_the_one_call_that_also_sets_the_DataContext()
    {
        string shell = Path.Combine(SourceRoot(), "SpectraEngine.Editor");
        Directory.Exists(shell).ShouldBeTrue($"expected the editor sources under {shell}");

        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(shell, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (IsComment(line)) continue;

                int assign = line.IndexOf("Tool.Content", StringComparison.Ordinal);
                if (assign < 0) continue;
                if (line.IndexOf('=', assign) < 0) continue;

                offenders.Add($"{Path.GetFileName(file)}({i + 1}): {line.Trim()}");
            }
        }

        offenders.ShouldBeEmpty(
            "a dock tool's Content must be assigned through SetToolContent, which sets the " +
            "DataContext with it; Dock supplies its own DataContext to tool content and a failed " +
            "binding leaves IsVisible at true rather than raising anything");
    }

    [Fact]
    public void The_pairing_lives_in_exactly_one_place()
    {
        // Without this the guard above passes over zero call sites.
        string window = Path.Combine(SourceRoot(), "SpectraEngine.Editor", "MainWindow.axaml.cs");
        string text = File.ReadAllText(window);

        text.ShouldContain("private void SetToolContent(",
            customMessage: "the one call that pairs a tool's content with its DataContext must exist");

        int calls = 0;
        int at = 0;
        while ((at = text.IndexOf("SetToolContent(", at, StringComparison.Ordinal)) >= 0)
        {
            calls++;
            at += "SetToolContent(".Length;
        }

        // Eight tools plus the declaration itself.
        calls.ShouldBe(9,
            "every dock tool in the window goes through the pairing; a new tool must join it");
    }

    private static bool IsComment(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("///", StringComparison.Ordinal);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("no solution file above the test binary");
    }
}
