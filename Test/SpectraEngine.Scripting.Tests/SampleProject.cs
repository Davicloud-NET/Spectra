using System.Text.Json;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Projects;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Core.Scripting.Tooling;
using SpectraEngine.Entities;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

// A project on disk as the editor leaves one after its first script was
// opened, with every sample script under its Assets folder.
internal sealed class SampleProject : IDisposable
{
    public const string AssetsFolder = ProjectFormat.AssetsFolder;

    private readonly ProjectFolder _folder = new();

    public SampleProject()
    {
        // What the editor writes, by the code it writes it with, and with
        // the entity classes a project of the built-in ones gets.
        IReadOnlyList<EntitySchema> schemas =
            EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(BuiltinEntities.Schemas)).Schemas;
        ScriptProjectReport report = ScriptProjectSetup.Prepare(
            Root, LuauDefinitions.Write(LuauScriptApi.Description.WithEntityClasses(schemas)));
        report.IsRefused.ShouldBeFalse(report.Describe());

        foreach (ScriptSample sample in ScriptSamples.All)
            _folder.Write(AssetsFolder + "/" + sample.ContentPath, sample.Text);
    }

    public string Root => _folder.Root;

    // What the settings file tells the extension, as the command line of
    // "luau-lsp analyze" says the same. So the file the editor writes is
    // what decides how the samples are checked.
    public string[] AnalyzeArguments()
    {
        using JsonDocument settings = JsonDocument.Parse(_folder.Read(ScriptProjectFiles.CodeSettings));
        JsonElement root = settings.RootElement;

        List<string> arguments = ["--platform", root.GetProperty("luau-lsp.platform.type").GetString().ShouldNotBeNull()];
        foreach (JsonProperty file in root.GetProperty("luau-lsp.types.definitionFiles").EnumerateObject())
            arguments.Add($"--definitions={file.Name}={file.Value.GetString()}");

        // With it on, the extension would look for the tree of a Roblox place.
        root.GetProperty("luau-lsp.sourcemap.enabled").GetBoolean().ShouldBeFalse();
        return [.. arguments];
    }

    public void Dispose() => _folder.Dispose();
}
