using SpectraEngine.Editor.Shell.Scripts;

namespace SpectraEngine.Editor.Tests;

/// <summary>What a new script's file holds when Insert Script makes it.</summary>
public sealed class ScriptTemplateTests
{
    private static string OnDisk() => File.ReadAllText(Path.Combine(
        IconFiles.SourceRoot(), "SpectraEngine.Editor", "Assets", "Templates", "Script.luau"));

    [Fact]
    public void The_text_is_the_template_file_in_the_editors_assets()
    {
        ScriptTemplate.Read().ShouldBe(OnDisk().Replace("\r\n", "\n"));
    }

    [Fact]
    public void It_prints_one_line_that_names_the_scripts_parent()
    {
        // So the first Play of a new script shows something in Output.
        string[] code = ScriptTemplate.Read().Split('\n')
            .Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal))
            .ToArray();

        code.ShouldBe(["print(\"Hello from \" .. script.Parent.Name)"]);
    }

    [Fact]
    public void It_is_the_same_bytes_whoever_built_the_editor()
    {
        string text = ScriptTemplate.Read();

        text.ShouldNotContain("\r");
        text.ShouldEndWith("\n");
    }
}
