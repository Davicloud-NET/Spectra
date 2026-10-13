using System.Text.Json;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Projects;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Core.Scripting.Tooling;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The text of the three small files a project gets beside its definitions
/// file, and that each says what the engine itself goes by.
/// </summary>
public sealed class ScriptProjectFilesTests
{
    [Fact]
    public void Luaurc_holds_the_alias_of_the_content_root_and_nothing_else()
    {
        // No language mode: strict or not is the choice of whoever writes the scripts.
        ScriptProjectFiles.LuauRcText().ShouldBe(
            "{\n" +
            "  \"aliases\": {\n" +
            "    \"assets\": \"./Assets\"\n" +
            "  }\n" +
            "}\n");
    }

    [Fact]
    public void The_settings_take_luau_lsp_off_roblox_and_name_the_definitions_file()
    {
        ScriptProjectFiles.CodeSettingsText().ShouldBe(
            "{\n" +
            "  \"luau-lsp.platform.type\": \"standard\",\n" +
            "  \"luau-lsp.types.definitionFiles\": {\n" +
            "    \"@spectra\": \"spectra.d.luau\"\n" +
            "  },\n" +
            "  \"luau-lsp.sourcemap.enabled\": false\n" +
            "}\n");
    }

    [Fact]
    public void The_extensions_file_recommends_luau_lsp()
    {
        ScriptProjectFiles.CodeExtensionsText().ShouldBe(
            "{\n" +
            "  \"recommendations\": [\n" +
            "    \"JohnnyMorganz.luau-lsp\"\n" +
            "  ]\n" +
            "}\n");
    }

    [Fact]
    public void The_alias_is_the_one_require_takes_for_the_content_root()
    {
        string asked = $"@{ScriptProjectFiles.AssetsAlias}/Shared/door";

        ModulePath.TryResolve(asked, "Scripts/main.luau", out string[] files, out string? refusal)
            .ShouldBeTrue(refusal);

        // From the content root, wherever the file that asks is.
        files.ShouldBe(["Shared/door.luau", "Shared/door/init.luau"]);
        ModulePath.Shape.ShouldEndWith("@" + ScriptProjectFiles.AssetsAlias);
    }

    [Fact]
    public void The_folder_the_alias_names_is_the_projects_content_root()
    {
        using var folder = new ProjectFolder();
        ProjectLayout project = ProjectLayout.Create(folder.Root, "Game");

        using JsonDocument luaurc = JsonDocument.Parse(ScriptProjectFiles.LuauRcText());
        string named = luaurc.RootElement
            .GetProperty("aliases").GetProperty(ScriptProjectFiles.AssetsAlias).GetString().ShouldNotBeNull();

        named.ShouldBe("./" + ContentRoot.DirectoryName);
        Path.GetFullPath(Path.Combine(project.Root, named)).ShouldBe(Path.GetFullPath(project.AssetsPath));
    }

    [Fact]
    public void A_script_in_a_level_reaches_a_module_through_the_alias()
    {
        using var level = new ScriptLevel();
        level.FileAt("Shared/far.luau", "return 'far'");
        level.ScriptAt("Scripts/main.luau", $"print(require('@{ScriptProjectFiles.AssetsAlias}/Shared/far'))");

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["far"]);
    }

    [Fact]
    public void The_settings_name_the_definitions_file_by_the_name_it_has_in_a_project()
    {
        using JsonDocument settings = JsonDocument.Parse(ScriptProjectFiles.CodeSettingsText());

        settings.RootElement
            .GetProperty("luau-lsp.types.definitionFiles").GetProperty(ScriptProjectFiles.DefinitionsName)
            .GetString().ShouldBe(LuauDefinitions.FileName);
        ScriptProjectFiles.Definitions.ShouldBe(LuauDefinitions.FileName);
    }
}
